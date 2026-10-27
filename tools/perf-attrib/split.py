import json,sys,statistics as st,collections
f=sys.argv[1]; lab=sys.argv[2] if len(sys.argv)>2 else 'warm 1280x800@100'
rows=[json.loads(l) for l in open(f)]
d=collections.defaultdict(list)
for r in rows:
    if r['name']=='attrib.frame-split' and r.get('label','').startswith(lab.split()[0]) and lab.split()[1] in r.get('label',''):
        d[r['view']].append(r)
print(f"{'view':22}{'loop':>8}{'tick':>8}{'jobs':>8}{'iter':>5}{'lu':>5}")
for v,l in d.items():
    m=lambda k: st.median(x[k] for x in l)
    print(f"{v:22}{m('loop-ms'):8.0f}{m('tick-ms'):8.0f}{m('jobs-ms'):8.0f}{m('iterations'):5.0f}{m('layout-updated'):5.0f}")
