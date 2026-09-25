using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.ViewModels;

internal static class UserFacingRefusal
{
    public static string MessageOf(Refusal refusal) => refusal.Code switch
    {
        "change.revision-conflict" => "The changes changed elsewhere. Refresh them and try again.",
        "change.invalid-identity" => "This change could not be added. Select the word again and retry.",
        "change.analysis-identity-required" => "Choose an analysis before adding this change.",
        "change.assessment-required" => "Run an Assessment before adding this change.",
        "change.baseline-missing" => "Capture a Baseline before collecting changes.",
        "change.wordform-missing" => "That word is no longer in the project. Refresh and try again.",
        "change.wordform-ambiguous" => "The project has more than one word with this spelling. Choose one in FieldWorks.",
        "change.wordform-changed" => "That word changed in the project. Refresh and try again.",
        "change.stored-analysis-missing" => "That analysis is no longer stored under this word. Refresh and choose another.",
        "change.assessment-stale" => "This Assessment is from an older Baseline. Run a new Assessment first.",
        "change.reading-missing" => "That parser reading is no longer available. Run the Assessment again.",
        "change.assessment-missing" => "This Assessment is no longer available. Run it again before adding the change.",
        "change.slot-occupied" => "Another pending change already addresses this word and analysis.",
        "change.cannot-compose" => "This change could not be added. Refresh the changes and try again.",
        "change.no-effect" => "This change would not alter the project.",
        "apply.project-in-use" => "FieldWorks has this project open. Close it before applying changes.",
        "apply.change-no-longer-fits" or "review.change-no-longer-fits" =>
            "One or more changes no longer fit. Remove those changes first.",
        "apply.reconciliation-needed" =>
            "Applying may have completed, but its result could not be confirmed. Check the project before retrying.",
        "apply.applied-content-mismatch" or "apply.drift" =>
            "Applying may have completed, but the project could not be verified. Check it before retrying.",
        "apply.regression" => "The check found worse results for words already measured. Review them before applying.",
        "apply.not-ready" or "apply.dry-run-missing" =>
            "The check did not give enough evidence to apply these changes. Check them again.",
        "review.changes-changed" => "The changes have changed. Check the numbers again before applying.",
        "review.reopen-failed" =>
            "The changes could not be applied or reopened. Inspect the FieldWorks project before trying again.",
        "review.measurement-incomplete" => "The check did not finish. Try it again.",
        "review.measurement-cancelled" => "The check was cancelled.",
        _ => "Motif could not complete this request. Review the project and try again.",
    };
}
