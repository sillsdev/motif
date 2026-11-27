-- Schema 61: sqlite_master of a store made by MotifSchema.Create at commit 1678ef395, the last commit with CurrentSchema = 61.
CREATE TABLE ActiveParse (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Token TEXT NOT NULL,
    ProcessId INTEGER NOT NULL,
    ProcessStartIdentity TEXT NOT NULL
);
CREATE TABLE AppliedIndex (
    ProposalId TEXT PRIMARY KEY,
    IntentDigest TEXT NOT NULL,
    AppliedUtc TEXT NOT NULL,
    RecordJson TEXT NULL
);
CREATE TABLE AssessedWords (
    AssessedWordId INTEGER PRIMARY KEY AUTOINCREMENT,
    AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
    OrdinalIndex INTEGER NOT NULL,
    Word TEXT NOT NULL,
    Outcome TEXT NOT NULL,
    ElapsedMs INTEGER NULL,
    ElapsedNs INTEGER NULL,
    RawSignature TEXT NULL,
    MorphologyJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
    CorrectnessJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
    ProjectStanding TEXT NULL,
    OccurrenceCount INTEGER NULL,
    ReadingGradesJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
    AnalysisComparisonJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
    MissedApprovedCount INTEGER NULL,
    MissedApprovedJson BLOB NULL REFERENCES AssessmentWordPayloads(Hash),
    IsIncomplete INTEGER NOT NULL DEFAULT 0 CHECK (IsIncomplete IN (0, 1)),
    Attempts INTEGER NULL,
    Passes INTEGER NULL,
    ParserWork INTEGER NULL
);
CREATE TABLE AssessmentInvocations (
    InvocationId TEXT PRIMARY KEY,
    EvidenceJson TEXT NOT NULL
);
CREATE TABLE AssessmentObjectTimings (
    AssessmentId TEXT NOT NULL PRIMARY KEY REFERENCES Assessments(AssessmentId),
    Detail BLOB NOT NULL,
    Evidence BLOB NULL
) WITHOUT ROWID;
CREATE TABLE AssessmentPins (
    AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
    PinnedBy TEXT NOT NULL,
    PinnedUtc TEXT NOT NULL,
    PRIMARY KEY (AssessmentId, PinnedBy)
);
CREATE TABLE AssessmentWordPayloads (
    Hash BLOB NOT NULL PRIMARY KEY,
    Payload BLOB NOT NULL
) WITHOUT ROWID;
CREATE TABLE Assessments (
    AssessmentId TEXT PRIMARY KEY,
    SelectionName TEXT NULL,
    SelectionWordsJson TEXT NULL,
    SelectionSha256 TEXT NULL,
    SelectionProvenanceJson TEXT NULL,
    OutcomeDigest TEXT NULL,
    SemanticDigest TEXT NULL,
    GrammarSourceSha256 TEXT NOT NULL,
    ModelFingerprint TEXT NULL,
    Pipeline TEXT NULL,
    DiagnosticCount INTEGER NULL,
    SavedUtc TEXT NOT NULL,
    ProposalId TEXT NULL REFERENCES Proposals(ProposalId),
    ProposalIntentDigest TEXT NULL,
    Assessor TEXT NOT NULL,
    Kind TEXT NOT NULL,
    ScopeJson TEXT NOT NULL,
    ScopeDigest TEXT NOT NULL,
    TokeniserName TEXT NULL,
    TokeniserVersion TEXT NULL,
    BaselineToken TEXT NOT NULL,
    CachePath TEXT NULL,
    CacheDigest TEXT NULL,
    InvocationId TEXT NULL REFERENCES AssessmentInvocations(InvocationId),
    ReplacesAssessmentId TEXT NULL REFERENCES Assessments(AssessmentId),
    SubjectKind TEXT NOT NULL CHECK (SubjectKind IN ('words', 'phonologyExamples')),
    SubjectJson TEXT NULL,
    CHECK ((SubjectKind = 'words' AND Kind != 'PhonologyExamples' AND SubjectJson IS NULL
        AND SelectionName IS NOT NULL AND SelectionWordsJson IS NOT NULL AND SelectionSha256 IS NOT NULL
        AND TokeniserName IS NOT NULL AND TokeniserVersion IS NOT NULL)
        OR (SubjectKind = 'phonologyExamples' AND Kind = 'PhonologyExamples' AND SubjectJson IS NOT NULL
        AND SelectionName IS NULL AND SelectionWordsJson IS NULL AND SelectionSha256 IS NULL
        AND SelectionProvenanceJson IS NULL AND TokeniserName IS NULL AND TokeniserVersion IS NULL
        AND CachePath IS NULL AND CacheDigest IS NULL AND InvocationId IS NULL AND ReplacesAssessmentId IS NULL
        AND ProposalIntentDigest IS NULL AND OutcomeDigest IS NULL AND SemanticDigest IS NULL
        AND ModelFingerprint IS NULL AND Pipeline IS NULL AND DiagnosticCount IS NULL))
);
CREATE TABLE BaselineSummaries (
    ProjectKey TEXT PRIMARY KEY REFERENCES Baselines(ProjectKey),
    SummaryJson TEXT NOT NULL
);
CREATE TABLE BaselineTextReadIndex (
    ProjectKey TEXT NOT NULL REFERENCES Baselines(ProjectKey),
    TextId TEXT NOT NULL,
    BundleDigest TEXT NOT NULL,
    IndexJson TEXT NOT NULL,
    PRIMARY KEY (ProjectKey, TextId)
);
CREATE TABLE BaselineTextWordforms (
    ProjectKey TEXT NOT NULL REFERENCES Baselines(ProjectKey),
    WordformId TEXT NOT NULL,
    BundleDigest TEXT NOT NULL,
    WordformJson TEXT NOT NULL,
    PRIMARY KEY (ProjectKey, WordformId)
);
CREATE TABLE BaselineTextWords (
    ProjectKey TEXT NOT NULL REFERENCES Baselines(ProjectKey),
    TextId TEXT NOT NULL,
    BundleDigest TEXT NOT NULL,
    TextJson TEXT NOT NULL,
    PRIMARY KEY (ProjectKey, TextId)
);
CREATE TABLE Baselines (
    ProjectKey TEXT PRIMARY KEY,
    ProjectIdentity TEXT NOT NULL,
    SemanticSnapshotDigest TEXT NOT NULL,
    ProjectionVersion TEXT NOT NULL,
    CapturedUtc TEXT NOT NULL,
    BundleDigest TEXT NOT NULL,
    CapturedHostSessionId TEXT NULL,
    CapturedEditGeneration INTEGER NULL,
    RootDirectory TEXT NOT NULL,
    FwDataPath TEXT NOT NULL,
    PublishedUtc TEXT NOT NULL,
    SourceLastWriteUtc TEXT NOT NULL,
    CHECK ((CapturedHostSessionId IS NULL) = (CapturedEditGeneration IS NULL)),
    CHECK (CapturedEditGeneration IS NULL OR CapturedEditGeneration >= 0)
);
CREATE TABLE Corpora (
    CorpusId TEXT PRIMARY KEY,
    ProvenanceJson TEXT NOT NULL
);
CREATE TABLE CorpusDocuments (
    CorpusId TEXT NOT NULL REFERENCES Corpora(CorpusId),
    DocumentId TEXT NOT NULL,
    OrdinalIndex INTEGER NOT NULL,
    Title TEXT NOT NULL,
    Source TEXT NOT NULL,
    Text TEXT NOT NULL,
    ContentSha256 TEXT NOT NULL,
    IngestedUtc TEXT NOT NULL,
    Licence TEXT NULL,
    CapabilitiesJson TEXT NULL,
    AttributesJson TEXT NULL,
    PRIMARY KEY (CorpusId, DocumentId)
);
CREATE TABLE CurrentParsimonyBundles (
    ProjectKey TEXT NOT NULL,
    InputKind TEXT NOT NULL CHECK (InputKind IN ('baseline','candidate')),
    BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
    PRIMARY KEY (ProjectKey, InputKind)
);
CREATE TABLE DefaultSelection (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    SelectionName TEXT NOT NULL REFERENCES NamedSelections(SelectionName)
);
CREATE TABLE GrammarChecks (
    BaselineToken TEXT NOT NULL,
    ProposalId TEXT NULL,
    IntentDigest TEXT NULL,
    SelectionSha256 TEXT NOT NULL,
    ParserStamp TEXT NULL,
    ResponseGzip BLOB NOT NULL,
    CheckedUtc TEXT NOT NULL
);
CREATE TABLE Jobs (
    JobId TEXT PRIMARY KEY,
    ProjectKey TEXT NOT NULL,
    Kind TEXT NOT NULL,
    Status TEXT NOT NULL,
    Attempt INTEGER NOT NULL DEFAULT 1 CHECK (Attempt > 0),
    LineageId TEXT NOT NULL,
    InputJson TEXT NOT NULL,
    ResultJson TEXT NULL,
    ProgressJson TEXT NULL,
    CancellationRequested INTEGER NOT NULL DEFAULT 0 CHECK (CancellationRequested IN (0, 1)),
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    Version INTEGER NOT NULL DEFAULT 0 CHECK (Version >= 0),
    DryRunPublished INTEGER NOT NULL DEFAULT 0 CHECK (DryRunPublished IN (0, 1)),
    DryRunJson TEXT NULL,
    FailureCategory TEXT NOT NULL DEFAULT 'none',
    NotBeforeUtc TEXT NULL,
    ArchivedUtc TEXT NULL,
    OwnerId TEXT NULL,
    ClaimToken TEXT NULL,
    LeaseUntilUtc TEXT NULL,
    HeartbeatUtc TEXT NULL,
    QueueOrder REAL NOT NULL DEFAULT (CAST((julianday('now') - 2440587.5) * 86400000.0 AS REAL))
);
CREATE TABLE MotifMetadata (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    FullFwDataPath TEXT NOT NULL,
    FieldWorksProjectIdentity TEXT NOT NULL,
    MinimumWorkerVersion TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    CurrentAssessmentId TEXT NULL,
    SetupSkippedUtc TEXT NULL
);
CREATE TABLE NamedSelections (
    SelectionName TEXT PRIMARY KEY,
    TextIdsJson TEXT NOT NULL,
    AddedWordsJson TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    TimeLimitMode TEXT NOT NULL CHECK (TimeLimitMode IN ('Estimated', 'Explicit')),
    ExplicitPerWordLimitMs INTEGER NULL CHECK (ExplicitPerWordLimitMs IS NULL OR ExplicitPerWordLimitMs > 0),
    PerWordParserWorkLimit INTEGER NULL CHECK (PerWordParserWorkLimit IS NULL OR PerWordParserWorkLimit > 0),
    Revision INTEGER NOT NULL CHECK (Revision > 0),
    AssessBudgetSeconds INTEGER NOT NULL DEFAULT 3600 CHECK (AssessBudgetSeconds >= 60),
    WordListJson TEXT NULL
);
CREATE TABLE ParsedAnalyses (
    AssessedWordId INTEGER NOT NULL REFERENCES AssessedWords(AssessedWordId),
    OrdinalIndex INTEGER NOT NULL,
    CategoryGuid TEXT NULL,
    MorphemeGuidsJson TEXT NOT NULL,
    RootIndex INTEGER NOT NULL,
    IdentityDigest TEXT NOT NULL
);
CREATE TABLE ParserRefusals (
    BaselineToken TEXT PRIMARY KEY,
    RefusalJson TEXT NOT NULL
);
CREATE TABLE ParsimonyBuildClaims (
    BundleId TEXT PRIMARY KEY,
    ProjectKey TEXT NOT NULL,
    JobId TEXT NOT NULL,
    ClaimToken TEXT NOT NULL,
    BaselineRootDirectory TEXT NOT NULL,
    WorkingDirectory TEXT NOT NULL,
    StagingDirectory TEXT NULL,
    FinalDirectory TEXT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE TABLE ParsimonyBundleReferences (
    BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
    ReferenceKind TEXT NOT NULL,
    ReferenceId TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    PRIMARY KEY (BundleId, ReferenceKind, ReferenceId)
);
CREATE TABLE ParsimonyBundles (
    BundleId TEXT PRIMARY KEY,
    BaselineTokenJson TEXT NOT NULL,
    ProjectKey TEXT NOT NULL,
    BaselineDigest TEXT NOT NULL,
    MaterialKey TEXT NOT NULL,
    EvidenceDirectory TEXT NOT NULL,
    GrammarFactsPath TEXT NOT NULL,
    EvidencePath TEXT NOT NULL,
    BaselineRootDirectory TEXT NOT NULL,
    InputKind TEXT NOT NULL CHECK (InputKind IN ('baseline','candidate')),
    CandidateIdentity TEXT NULL,
    ModelFingerprint TEXT NOT NULL,
    GrammarFactsSchemaVersion INTEGER NOT NULL CHECK (GrammarFactsSchemaVersion > 0),
    GrammarFactsSha256 TEXT NOT NULL,
    EvidenceSchemaVersion INTEGER NOT NULL CHECK (EvidenceSchemaVersion > 0),
    EvidenceSha256 TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    State TEXT NOT NULL CHECK (State IN ('available','deleting','missing','corrupt')) DEFAULT 'available',
    UnavailableReason TEXT NULL,
    CHECK ((InputKind = 'candidate') = (CandidateIdentity IS NOT NULL))
);
CREATE TABLE ParsimonyFrozenViews (
    BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
    ViewCode TEXT NOT NULL,
    FilterDigest TEXT NOT NULL,
    ViewVersion INTEGER NOT NULL CHECK (ViewVersion > 0),
    ItemsDigest TEXT NOT NULL,
    RowCount INTEGER NOT NULL CHECK (RowCount >= 0),
    ResultJson TEXT NOT NULL,
    PRIMARY KEY (BundleId, ViewCode, FilterDigest)
);
CREATE TABLE ParsimonyReaderLeases (
    LeaseId TEXT PRIMARY KEY,
    BundleId TEXT NOT NULL REFERENCES ParsimonyBundles(BundleId) ON DELETE CASCADE,
    OwnerId TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    HeartbeatUtc TEXT NOT NULL,
    ExpiresUtc TEXT NOT NULL
);
CREATE TABLE ParsimonySelection (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    SelectionName TEXT NOT NULL REFERENCES NamedSelections(SelectionName)
);
CREATE TABLE PendingChangeFits (
    DraftRevision TEXT NOT NULL,
    ProjectLastWriteUtcTicks INTEGER NOT NULL,
    BaselineIdentity TEXT NOT NULL,
    FitSummaryJson TEXT NOT NULL,
    PRIMARY KEY (DraftRevision, ProjectLastWriteUtcTicks, BaselineIdentity)
);
CREATE TABLE PhonologyAssessmentBindings (
    AssessmentId TEXT NOT NULL PRIMARY KEY REFERENCES Assessments(AssessmentId),
    RevisionId TEXT NOT NULL REFERENCES PhonologyExampleSetRevisions(RevisionId),
    HeaderJson TEXT NOT NULL,
    UNIQUE (AssessmentId, RevisionId)
);
CREATE TABLE PhonologyAssessmentDependencies (
    AssessmentId TEXT NOT NULL REFERENCES PhonologyAssessmentBindings(AssessmentId),
    Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
    Path TEXT NOT NULL,
    Sha256 TEXT NOT NULL,
    PRIMARY KEY (AssessmentId, Ordinal),
    UNIQUE (AssessmentId, Path)
);
CREATE TABLE PhonologyAssessmentRows (
    AssessmentId TEXT NOT NULL,
    RevisionId TEXT NOT NULL,
    Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
    ExampleId TEXT NOT NULL,
    EvidenceJson TEXT NOT NULL,
    SummaryJson TEXT NOT NULL,
    PRIMARY KEY (AssessmentId, Ordinal),
    UNIQUE (AssessmentId, ExampleId),
    FOREIGN KEY (AssessmentId, RevisionId) REFERENCES PhonologyAssessmentBindings(AssessmentId, RevisionId),
    FOREIGN KEY (RevisionId, Ordinal, ExampleId) REFERENCES PhonologyExamples(RevisionId, Ordinal, ExampleId)
);
CREATE TABLE PhonologyExampleSetRevisions (
    RevisionId TEXT PRIMARY KEY,
    SetId TEXT NOT NULL REFERENCES PhonologyExampleSets(SetId),
    RevisionNumber INTEGER NOT NULL CHECK (RevisionNumber > 0),
    ExecutableSha256 TEXT NOT NULL,
    CitedRevisionSha256 TEXT NOT NULL,
    ExampleCount INTEGER NOT NULL CHECK (ExampleCount BETWEEN 1 AND 500),
    ProposalId TEXT NULL REFERENCES Proposals(ProposalId),
    SavedUtc TEXT NOT NULL,
    UNIQUE (SetId, RevisionId)
);
CREATE TABLE PhonologyExampleSets (
    SetId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    CurrentRevisionId TEXT NOT NULL,
    FOREIGN KEY (SetId, CurrentRevisionId)
        REFERENCES PhonologyExampleSetRevisions(SetId, RevisionId) DEFERRABLE INITIALLY DEFERRED
);
CREATE TABLE PhonologyExamples (
    RevisionId TEXT NOT NULL REFERENCES PhonologyExampleSetRevisions(RevisionId),
    Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
    ExampleId TEXT NOT NULL,
    Underlying TEXT NOT NULL,
    ExpectedJson TEXT NOT NULL,
    ContextJson TEXT NOT NULL,
    CitationJson TEXT NULL,
    PRIMARY KEY (RevisionId, Ordinal),
    UNIQUE (RevisionId, ExampleId),
    UNIQUE (RevisionId, Ordinal, ExampleId)
);
CREATE TABLE ProposalDifferenceBuckets (
    DifferenceId TEXT NOT NULL REFERENCES ProposalDifferences(DifferenceId) ON DELETE CASCADE,
    CaseKey TEXT NOT NULL,
    Bucket TEXT NOT NULL,
    PRIMARY KEY (DifferenceId, CaseKey, Bucket)
);
CREATE TABLE ProposalDifferenceCases (
    DifferenceId TEXT NOT NULL REFERENCES ProposalDifferences(DifferenceId) ON DELETE CASCADE,
    CaseKey TEXT NOT NULL,
    Word TEXT NOT NULL,
    OccurrenceCount INTEGER NOT NULL CHECK (OccurrenceCount >= 0),
    BeforeState TEXT NOT NULL,
    AfterState TEXT NOT NULL,
    CouldBeBad INTEGER NOT NULL CHECK (CouldBeBad IN (0, 1)),
    FactsJson TEXT NOT NULL,
    FactsDigest TEXT NOT NULL,
    PRIMARY KEY (DifferenceId, CaseKey)
);
CREATE TABLE ProposalDifferenceOccurrences (
    DifferenceId TEXT NOT NULL REFERENCES ProposalDifferences(DifferenceId) ON DELETE CASCADE,
    TextId TEXT NOT NULL,
    SegmentId TEXT NOT NULL,
    OccurrenceIndex INTEGER NOT NULL CHECK (OccurrenceIndex >= 0),
    LineNumber INTEGER NOT NULL,
    CaseKey TEXT NOT NULL,
    PRIMARY KEY (DifferenceId, TextId, SegmentId, OccurrenceIndex)
);
CREATE TABLE ProposalDifferences (
    DifferenceId TEXT PRIMARY KEY,
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    TrialJobId TEXT NOT NULL,
    BeforeAssessmentId TEXT NOT NULL,
    AfterAssessmentId TEXT NOT NULL,
    TextId TEXT NOT NULL,
    Reference TEXT NOT NULL,
    BaselineToken TEXT NOT NULL,
    SummaryJson TEXT NOT NULL,
    SummaryDigest TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL,
    WordDetailDroppedUtc TEXT
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
CREATE TABLE ReadOccurrences (
    TextId TEXT NOT NULL,
    ParagraphId TEXT NOT NULL,
    SegmentId TEXT NOT NULL,
    WordIndex INTEGER NOT NULL CHECK (WordIndex >= 0),
    FingerprintJson TEXT NOT NULL,
    PRIMARY KEY (TextId, ParagraphId, SegmentId, WordIndex)
);
CREATE TABLE Receipts (
    ReceiptId TEXT PRIMARY KEY,
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    IntentDigest TEXT NOT NULL,
    ReceiptJson TEXT NOT NULL,
    RecordedUtc TEXT NOT NULL
);
CREATE TABLE Reports (
    ReportId TEXT PRIMARY KEY,
    ProposalId TEXT NULL REFERENCES Proposals(ProposalId),
    AssessmentId TEXT NULL REFERENCES Assessments(AssessmentId),
    ReportJson TEXT NOT NULL,
    EvidenceJson TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    Kind TEXT NULL,
    RenderedText TEXT NULL
);
CREATE TABLE RetainedInvocationMembers (
    InvocationId TEXT NOT NULL REFERENCES RetainedInvocations(InvocationId),
    Kind TEXT NOT NULL,
    AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
    PRIMARY KEY (InvocationId, Kind),
    UNIQUE (AssessmentId)
);
CREATE TABLE RetainedInvocations (
    InvocationId TEXT PRIMARY KEY,
    ProjectKey TEXT NOT NULL,
    BaselineToken TEXT NOT NULL,
    BaselineRootDirectory TEXT NOT NULL,
    BaselineFwDataPath TEXT NOT NULL,
    BaselineSourceLastWriteUtc TEXT NOT NULL,
    BaselinePublishedUtc TEXT NOT NULL,
    SavedUtc TEXT NOT NULL,
    SelectionDescriptorJson TEXT NOT NULL,
    SelectionDescriptorSha256 TEXT NOT NULL,
    ExpectedKindsJson TEXT NOT NULL,
    Assessor TEXT NOT NULL,
    ScopeJson TEXT NOT NULL,
    ScopeDigest TEXT NOT NULL,
    ArtifactInvocationId TEXT NOT NULL REFERENCES AssessmentInvocations(InvocationId)
);
CREATE TABLE ReviewMarks (
    ProposalId TEXT NOT NULL,
    EntryKey TEXT NOT NULL,
    ContentDigest TEXT NOT NULL,
    EvidenceDigest TEXT NOT NULL,
    LookedAtUtc TEXT NOT NULL,
    PRIMARY KEY (ProposalId, EntryKey)
);
CREATE INDEX IX_AssessedWords_AnalysisComparisonJson ON AssessedWords(AnalysisComparisonJson) WHERE AnalysisComparisonJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_Assessment ON AssessedWords(AssessmentId);
CREATE INDEX IX_AssessedWords_CorrectnessJson ON AssessedWords(CorrectnessJson) WHERE CorrectnessJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_MissedApprovedJson ON AssessedWords(MissedApprovedJson) WHERE MissedApprovedJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_MorphologyJson ON AssessedWords(MorphologyJson) WHERE MorphologyJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_ReadingGradesJson ON AssessedWords(ReadingGradesJson) WHERE ReadingGradesJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_Word ON AssessedWords(AssessmentId, Word);
CREATE INDEX IX_Assessments_Kind ON Assessments(Kind);
CREATE INDEX IX_Assessments_Proposal ON Assessments(ProposalId);
CREATE UNIQUE INDEX IX_GrammarChecks_Project ON GrammarChecks(BaselineToken) WHERE ProposalId IS NULL;
CREATE UNIQUE INDEX IX_GrammarChecks_Proposal
    ON GrammarChecks(BaselineToken, ProposalId, IntentDigest) WHERE ProposalId IS NOT NULL;
CREATE INDEX IX_Jobs_Lease ON Jobs(Status, LeaseUntilUtc);
CREATE UNIQUE INDEX IX_Jobs_Lineage_Attempt ON Jobs(LineageId, Attempt);
CREATE INDEX IX_Jobs_QueueOrder ON Jobs(QueueOrder);
CREATE INDEX IX_Jobs_Status_Updated ON Jobs(Status, UpdatedUtc);
CREATE INDEX IX_ParsedAnalyses_Word ON ParsedAnalyses(AssessedWordId);
CREATE INDEX IX_ParsimonyBuildClaims_Project ON ParsimonyBuildClaims(ProjectKey);
CREATE INDEX IX_ParsimonyBundles_ProjectCreated ON ParsimonyBundles(ProjectKey, CreatedUtc);
CREATE INDEX IX_ParsimonyReaderLeases_BundleExpiry ON ParsimonyReaderLeases(BundleId, ExpiresUtc);
CREATE UNIQUE INDEX IX_PhonologyExampleSetRevisions_SetRevision
    ON PhonologyExampleSetRevisions(SetId, RevisionNumber);
CREATE UNIQUE INDEX IX_PhonologyExampleSets_Name ON PhonologyExampleSets(Name);
CREATE UNIQUE INDEX IX_PhonologyExamples_RevisionExampleId ON PhonologyExamples(RevisionId, ExampleId);
CREATE INDEX IX_ProposalDifferenceBuckets_Bucket ON ProposalDifferenceBuckets(DifferenceId, Bucket, CaseKey);
CREATE INDEX IX_ProposalDifferenceOccurrences_Order
    ON ProposalDifferenceOccurrences(DifferenceId, TextId, LineNumber, OccurrenceIndex);
CREATE UNIQUE INDEX IX_ProposalDifferences_ProposalText ON ProposalDifferences(ProposalId, TextId, Reference);
CREATE UNIQUE INDEX IX_Proposals_DraftName ON Proposals(DraftName);
CREATE UNIQUE INDEX IX_Proposals_OpenRole ON Proposals(Role) WHERE DraftName IS NOT NULL;
CREATE INDEX IX_RetainedInvocationMembers_Assessment ON RetainedInvocationMembers(AssessmentId);
CREATE INDEX IX_RetainedInvocations_Project ON RetainedInvocations(ProjectKey);
CREATE TRIGGER TR_PhonologyExampleSetRevisions_ImmutableDelete
BEFORE DELETE ON PhonologyExampleSetRevisions
BEGIN
    SELECT RAISE(ABORT, 'Example Set revisions are immutable');
END;
CREATE TRIGGER TR_PhonologyExampleSetRevisions_ImmutableUpdate
BEFORE UPDATE ON PhonologyExampleSetRevisions
BEGIN
    SELECT RAISE(ABORT, 'Example Set revisions are immutable');
END;
CREATE TRIGGER TR_PhonologyExampleSets_NameImmutable
BEFORE UPDATE OF Name ON PhonologyExampleSets
BEGIN
    SELECT RAISE(ABORT, 'Example Set names are immutable');
END;
CREATE TRIGGER TR_PhonologyExamples_Complete
BEFORE INSERT ON PhonologyExamples
WHEN (SELECT COUNT(*) FROM PhonologyExamples WHERE RevisionId = NEW.RevisionId) >=
     (SELECT ExampleCount FROM PhonologyExampleSetRevisions WHERE RevisionId = NEW.RevisionId)
BEGIN
    SELECT RAISE(ABORT, 'Example Set revision is complete');
END;
CREATE TRIGGER TR_PhonologyExamples_ImmutableDelete
BEFORE DELETE ON PhonologyExamples
BEGIN
    SELECT RAISE(ABORT, 'Example Set examples are immutable');
END;
CREATE TRIGGER TR_PhonologyExamples_ImmutableUpdate
BEFORE UPDATE ON PhonologyExamples
BEGIN
    SELECT RAISE(ABORT, 'Example Set examples are immutable');
END;
