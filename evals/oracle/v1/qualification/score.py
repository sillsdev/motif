"""Rehearse independent Judge accounting from frozen fixtures without model calls."""

import argparse
import json
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f"Duplicate JSON field: {key}")
        result[key] = value
    return result


def read_rows(path):
    rows = {}
    for line in Path(path).read_text(encoding="utf-8").splitlines():
        row = json.loads(line, object_pairs_hook=unique_object)
        case_id = row["case_id"]
        require(case_id not in rows, f"Duplicate case: {case_id}")
        rows[case_id] = row
    require(rows, f"Empty input: {path}")
    return rows


def decision(criteria, prohibitions):
    if "not_met" in criteria.values() or "violated" in prohibitions.values():
        return "fail"
    if "insufficient_evidence" in criteria.values() or "insufficient_evidence" in prohibitions.values():
        return "unscored"
    return "pass"


def validate_points(rows, expected_ids, allowed, answer, quoted_status):
    require(isinstance(rows, list), "Findings must be arrays")
    result = {}
    for row in rows:
        require(set(row) == {"id", "status", "quote", "reason"}, "Unexpected finding shape")
        point_id, status, quote = row["id"], row["status"], row["quote"]
        require(point_id in expected_ids and point_id not in result, f"Unknown/duplicate point: {point_id}")
        require(status in allowed, f"Unknown status: {status}")
        require(isinstance(row["reason"], str) and row["reason"].strip(), "Missing finding reason")
        require(quote is None or isinstance(quote, str), "Quote must be a string or null")
        if quote is not None:
            require(quote.strip() and quote in answer, "Quote is absent from the frozen answer")
        if status == quoted_status:
            require(quote is not None, f"{status} needs an exact quote")
        if status == "clear":
            require(quote is None, "A clear prohibition must use a null quote")
        result[point_id] = status
    require(set(result) == expected_ids, "Missing required finding")
    return result


def validate_vote(vote, case, judge):
    require(set(vote) == {"format", "oracle_version", "case_id", "judge", "criteria", "prohibitions", "decision"},
            "Unexpected Judge response shape")
    require(type(vote["format"]) is int and vote["format"] == 1 and vote["oracle_version"] == "v1",
            "Wrong Judge format/version")
    require(vote["judge"] == judge and vote["case_id"] == case["case_id"], "Wrong Judge/case")
    criteria = validate_points(vote["criteria"], {p["id"] for p in case["key"]["criteria"]},
                               {"met", "not_met", "insufficient_evidence"}, case["answer"], "met")
    prohibitions = validate_points(vote["prohibitions"], {p["id"] for p in case["key"]["prohibitions"]},
                                   {"clear", "violated", "insufficient_evidence"}, case["answer"], "violated")
    require(vote["decision"] == decision(criteria, prohibitions), "Decision contradicts findings")
    return criteria, prohibitions, vote["decision"]


def score(cases, anchors, opus, sol):
    require(set(cases) == set(anchors) == set(opus) == set(sol), "Case sets differ; missing votes cannot be dropped")
    members = {j: {"decision_matches": 0, "criterion_matches": 0, "prohibition_matches": 0,
                   "false_passes": 0, "false_failures": 0, "critical_false_passes": 0,
                   "critical_prohibition_misses": 0} for j in ("opus", "sol")}
    pairs = {"covered": 0, "decision_matches": 0, "false_passes": 0, "false_failures": 0}
    disputes, abstentions = [], []
    eligible, excluded, criterion_count, prohibition_count = 0, 0, 0, 0
    for case_id, case in cases.items():
        require(case["format"] == 1 and case["oracle_version"] == "v1", "Wrong case format/version")
        require(case["key"]["state"] == "fixture" and anchors[case_id]["status"] == "fixture",
                "This scorer accepts authored fixtures only, not accepted Oracle keys")
        anchor = anchors[case_id]
        for section, allowed in (("criteria", {"met", "not_met", "insufficient_evidence"}),
                                 ("prohibitions", {"clear", "violated", "insufficient_evidence"})):
            ids = [p["id"] for p in case["key"][section]]
            require(len(ids) == len(set(ids)) and set(ids) == set(anchor[section]), "Invalid key/anchor points")
            require(all(s in allowed for s in anchor[section].values()), "Invalid anchor status")
        require(anchor["decision"] == decision(anchor["criteria"], anchor["prohibitions"]), "Invalid anchor decision")
        votes = {j: validate_vote(rows[case_id], case, j) for j, rows in (("opus", opus), ("sol", sol))}
        if anchor["decision"] == "unscored":
            excluded += 1
            abstentions.append({"case_id": case_id, "both_abstain": all(v[2] == "unscored" for v in votes.values())})
            continue
        eligible += 1
        criterion_count += len(anchor["criteria"])
        prohibition_count += len(anchor["prohibitions"])
        critical_negative = any(p["critical"] and anchor["prohibitions"][p["id"]] == "violated"
                                for p in case["key"]["prohibitions"])
        for judge, (criteria, prohibitions, label) in votes.items():
            stats = members[judge]
            stats["decision_matches"] += label == anchor["decision"]
            stats["criterion_matches"] += sum(s == anchor["criteria"][i] for i, s in criteria.items())
            stats["prohibition_matches"] += sum(s == anchor["prohibitions"][i] for i, s in prohibitions.items())
            stats["false_passes"] += label == "pass" and anchor["decision"] == "fail"
            stats["false_failures"] += label == "fail" and anchor["decision"] == "pass"
            stats["critical_false_passes"] += label == "pass" and critical_negative
            stats["critical_prohibition_misses"] += sum(p["critical"] and anchor["prohibitions"][p["id"]] == "violated"
                                                        and prohibitions[p["id"]] != "violated"
                                                        for p in case["key"]["prohibitions"])
        if votes["opus"] == votes["sol"] and votes["opus"][2] != "unscored":
            label = votes["opus"][2]
            pairs["covered"] += 1
            pairs["decision_matches"] += label == anchor["decision"]
            pairs["false_passes"] += label == "pass" and anchor["decision"] == "fail"
            pairs["false_failures"] += label == "fail" and anchor["decision"] == "pass"
        else:
            disputes.append(case_id)
    for stats in members.values():
        stats["decision_agreement"] = stats["decision_matches"] / eligible if eligible else None
    pairs["coverage"] = pairs["covered"] / eligible if eligible else None
    pairs["agreement"] = pairs["decision_matches"] / eligible if eligible else None
    return {"status": "fixture_only", "live_models_qualified": False, "cases": len(cases),
            "eligible_anchors": eligible, "unscored_anchors": excluded,
            "criterion_denominator": criterion_count, "prohibition_denominator": prohibition_count,
            "members": members, "pair": pairs, "unscored_pair_cases": disputes, "anchor_abstentions": abstentions}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("cases", "anchors", "opus", "sol"):
        parser.add_argument(f"--{name}", required=True)
    args = parser.parse_args()
    try:
        result = score(*(read_rows(getattr(args, name)) for name in ("cases", "anchors", "opus", "sol")))
    except (ValueError, KeyError, TypeError, OSError) as error:
        parser.exit(2, f"Invalid qualification input: {error}\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
