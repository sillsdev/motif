CREATE TABLE MotifMetadata (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    FullFwDataPath TEXT NOT NULL,
    FieldWorksProjectIdentity TEXT NOT NULL,
    MinimumWorkerVersion TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    CurrentAssessmentId TEXT NULL,
    SetupSkippedUtc TEXT NULL
);
CREATE TABLE ProposalRevisions (
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    IntentDigest TEXT NOT NULL,
    ProposalJson BLOB NOT NULL,
    CreatedUtc TEXT NOT NULL,
    PRIMARY KEY (ProposalId, IntentDigest)
);
CREATE TABLE Proposals (
    ProposalId TEXT PRIMARY KEY,
    CurrentIntentDigest TEXT NULL,
    Status TEXT NOT NULL,
    Label TEXT NULL,
    Rationale TEXT NULL,
    RationaleIntentDigest TEXT NULL,
    RationaleUpdatedUtc TEXT NULL,
    Note TEXT NULL,
    NoteIntentDigest TEXT NULL,
    NoteUpdatedUtc TEXT NULL,
    SupersededBy TEXT NULL,
    AnchorJson TEXT NULL,
    ArchivedUtc TEXT NULL,
    DraftName TEXT NULL,
    DraftJson TEXT NULL,
    Role TEXT NULL CHECK (Role IS NULL OR Role = 'pending-changes')
);
