-- Schema 78: sqlite_master of a store made by MotifSchema.Create at commit 80206be5c, the last commit with CurrentSchema = 78.
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
    ParserWork INTEGER NULL,
    LowercaseOf TEXT NULL
);
CREATE TABLE AssessmentInvocations (
    InvocationId TEXT PRIMARY KEY,
    EvidenceJson TEXT NOT NULL,
    InputProfileJson TEXT NULL
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
CREATE TABLE AssessmentWordSetBindings (
    AssessmentId TEXT NOT NULL PRIMARY KEY REFERENCES Assessments(AssessmentId),
    OwnerAssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId)
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
CREATE TABLE BeforeAssessmentBinding (
    Id INTEGER PRIMARY KEY,
    AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId),
    SemanticIdentityJson TEXT NOT NULL
);
CREATE TABLE Corpora (
    CorpusId TEXT PRIMARY KEY,
    ProvenanceJson TEXT NOT NULL,
    FileBacked INTEGER NOT NULL DEFAULT 0
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
    FileBacked INTEGER NOT NULL DEFAULT 0,
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
CREATE TABLE GrammarCoverageMeasurements (StampId TEXT PRIMARY KEY, Payload TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE GrammarCoverageNoAnalysisRank (
    StampId TEXT NOT NULL REFERENCES GrammarCoverageMeasurements(StampId) ON DELETE CASCADE,
    NegativeWeight INTEGER NOT NULL, WordformId TEXT NOT NULL,
    WritingSystemId TEXT NOT NULL, FormNfd TEXT NOT NULL,
    PRIMARY KEY (StampId, NegativeWeight, WordformId)
) WITHOUT ROWID;
CREATE TABLE GrammarCoverageSegmentations (Id TEXT PRIMARY KEY, Payload TEXT NOT NULL) WITHOUT ROWID;
CREATE TABLE Hypotheses (
    HypothesisId TEXT PRIMARY KEY,
    Seq INTEGER NOT NULL,
    Pattern TEXT NULL,
    Statement TEXT NOT NULL,
    Mechanism TEXT NOT NULL,
    ChangeText TEXT NOT NULL,
    Environment TEXT NOT NULL DEFAULT '',
    HostClass TEXT NOT NULL DEFAULT '',
    Slot TEXT NOT NULL DEFAULT '',
    Neighbour TEXT NULL,
    PredictionsJson TEXT NOT NULL DEFAULT '[]',
    Falsifier TEXT NULL,
    Exceptions TEXT NULL,
    ObjectsAdded INTEGER NULL,
    AttestedTypesGained INTEGER NULL,
    QuestionsJson TEXT NOT NULL DEFAULT '[]',
    Status TEXT NOT NULL DEFAULT 'proposed' CHECK (Status IN ('proposed', 'passed', 'failed', 'superseded')),
    Decision TEXT NULL,
    SupersededBy TEXT NULL,
    GdrId TEXT NULL,
    GdrSection TEXT NULL,
    GdrNone TEXT NULL,
    SealedVerdict TEXT NULL CHECK (SealedVerdict IS NULL OR SealedVerdict IN ('match', 'mismatch')),
    VerdictSource TEXT NULL,
    WordQuestionsAsked INTEGER NULL,
    ObjectsBefore INTEGER NULL,
    TypesBefore INTEGER NULL,
    ObjectsAfter INTEGER NULL,
    TypesAfter INTEGER NULL,
    AgentHours REAL NULL,
    AlternativeHours REAL NULL,
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
CREATE TABLE HypothesisProposals (
    HypothesisId TEXT NOT NULL,
    ProposalId TEXT NOT NULL,
    LinkedUtc TEXT NOT NULL,
    PRIMARY KEY (HypothesisId, ProposalId)
);
CREATE TABLE HypothesisTests (
    HypothesisId TEXT NOT NULL,
    TestNumber INTEGER NOT NULL,
    Verdict TEXT NOT NULL CHECK (Verdict IN ('PASS', 'FAIL', 'UNDECIDED')),
    PredictedForms INTEGER NOT NULL,
    RuleHits INTEGER NOT NULL,
    ControlHitsJson TEXT NOT NULL,
    Margin INTEGER NOT NULL,
    TrialAttestedLosses INTEGER NULL,
    TrialNewReadings INTEGER NULL,
    TrialWorkSteps INTEGER NULL,
    Command TEXT NULL,
    RecordedUtc TEXT NOT NULL,
    PRIMARY KEY (HypothesisId, TestNumber)
);
CREATE TABLE IndexedCorpusDocuments (
    Generation TEXT NOT NULL REFERENCES IndexedCorpusGenerations(Id) ON DELETE CASCADE,
    Ordinal INTEGER NOT NULL,
    DocumentId TEXT NOT NULL,
    Container TEXT NOT NULL,
    Position INTEGER NOT NULL,
    RawHash TEXT NOT NULL,
    TextHash TEXT NOT NULL,
    Metadata TEXT,
    ShardId INTEGER NOT NULL REFERENCES IndexedCorpusShards(Id),
    Tokens INTEGER NOT NULL,
    PRIMARY KEY (Generation, Ordinal),
    UNIQUE (Generation, DocumentId)
) WITHOUT ROWID;
CREATE TABLE IndexedCorpusForms (
    Id INTEGER PRIMARY KEY,
    FormKey TEXT NOT NULL UNIQUE,
    WritingSystem TEXT NOT NULL,
    FormNfd TEXT
);
CREATE TABLE IndexedCorpusGenerations (
    Id TEXT NOT NULL PRIMARY KEY,
    SourceId TEXT NOT NULL,
    BindingJson TEXT NOT NULL,
    InventoryJson TEXT NOT NULL,
    DocumentDigest TEXT NOT NULL,
    DocumentCount INTEGER NOT NULL,
    TypeCount INTEGER NOT NULL,
    TokenCount INTEGER NOT NULL,
    Published INTEGER NOT NULL CHECK (Published IN (0, 1)),
    PublishedUtc TEXT NOT NULL,
    ProjectionValid INTEGER NOT NULL CHECK (ProjectionValid IN (0, 1))
);
CREATE TABLE IndexedCorpusHeads (
    SourceId TEXT NOT NULL PRIMARY KEY,
    Generation TEXT NOT NULL REFERENCES IndexedCorpusGenerations(Id),
    CheckedUtc TEXT NOT NULL,
    Freshness INTEGER NOT NULL
);
CREATE TABLE IndexedCorpusOccurrences (
    ShardId INTEGER NOT NULL REFERENCES IndexedCorpusShards(Id) ON DELETE CASCADE,
    Ordinal INTEGER NOT NULL,
    FormId INTEGER NOT NULL REFERENCES IndexedCorpusForms(Id),
    Segment INTEGER NOT NULL,
    Word INTEGER NOT NULL,
    ByteOffset INTEGER CHECK (ByteOffset > 0),
    ByteLength INTEGER CHECK (ByteLength > 0),
    Verse TEXT,
    Surface TEXT,
    PRIMARY KEY (ShardId, Ordinal)
) WITHOUT ROWID;
CREATE TABLE IndexedCorpusShardWeights (
    ShardId INTEGER NOT NULL REFERENCES IndexedCorpusShards(Id) ON DELETE CASCADE,
    FormId INTEGER NOT NULL REFERENCES IndexedCorpusForms(Id),
    Frequency INTEGER NOT NULL CHECK (Frequency > 0),
    PRIMARY KEY (ShardId, FormId)
) WITHOUT ROWID;
CREATE TABLE IndexedCorpusShards (
    Id INTEGER PRIMARY KEY,
    StageGeneration TEXT NOT NULL REFERENCES IndexedCorpusGenerations(Id),
    Digest TEXT NOT NULL
);
CREATE TABLE IndexedCorpusWeights (
    Generation TEXT NOT NULL REFERENCES IndexedCorpusGenerations(Id) ON DELETE CASCADE,
    FormId INTEGER NOT NULL REFERENCES IndexedCorpusForms(Id),
    Frequency INTEGER NOT NULL CHECK (Frequency > 0),
    PRIMARY KEY (Generation, FormId)
) WITHOUT ROWID;
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
CREATE TABLE ProgressState (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    Revision TEXT NOT NULL,
    ContentRevision TEXT NOT NULL,
    JudgementRevision TEXT NOT NULL,
    SourceLastWriteUtc TEXT NOT NULL,
    PendingReceiptId TEXT NULL,
    TotalsJson TEXT NOT NULL,
    ExcludedJson TEXT NOT NULL,
    SourceIdentityJson TEXT NULL
);
CREATE TABLE ProgressTexts (
    TextId TEXT PRIMARY KEY,
    Title TEXT NOT NULL,
    CountsJson TEXT NOT NULL,
    IncidenceJson TEXT NOT NULL,
    ExcludedJson TEXT NOT NULL
);
CREATE TABLE ProposalComments (
    CommentId INTEGER PRIMARY KEY AUTOINCREMENT,
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    Kind TEXT NOT NULL DEFAULT 'standard' CHECK (Kind IN ('standard', 'system')),
    AuthorLabel TEXT NULL,
    PersonName TEXT NOT NULL,
    Text TEXT NOT NULL,
    IntentDigest TEXT NULL,
    CreatedUtc TEXT NOT NULL,
    EditedUtc TEXT NULL
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
CREATE TABLE ProposalPictures (
    PictureId INTEGER PRIMARY KEY AUTOINCREMENT,
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    CommentId INTEGER NULL REFERENCES ProposalComments(CommentId),
    MediaType TEXT NOT NULL,
    Width INTEGER NOT NULL CHECK (Width > 0),
    Height INTEGER NOT NULL CHECK (Height > 0),
    Bytes BLOB NOT NULL,
    CreatedUtc TEXT NOT NULL,
    Caption TEXT NULL
);
CREATE TABLE ProposalRationaleRevisions (
    RevisionId INTEGER PRIMARY KEY AUTOINCREMENT,
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    Text TEXT NOT NULL,
    IntentDigest TEXT NULL,
    AuthorLabel TEXT NULL,
    PersonName TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL
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
    SupersededBy TEXT NULL,
    AnchorJson TEXT NULL,
    ArchivedUtc TEXT NULL,
    DraftName TEXT NULL,
    DraftJson TEXT NULL,
    Role TEXT NULL CHECK (Role IS NULL OR Role = 'pending-changes'),
    RequestId TEXT NULL
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
CREATE TABLE Requests (
    RequestId TEXT PRIMARY KEY,
    TargetKind TEXT NOT NULL CHECK (TargetKind IN ('word', 'warning', 'matrix-cell')),
    TargetId TEXT NOT NULL,
    TargetLabel TEXT NOT NULL,
    Note TEXT NULL,
    Status TEXT NOT NULL DEFAULT 'open' CHECK (Status IN ('open', 'answered', 'withdrawn')),
    CreatedUtc TEXT NOT NULL,
    ClosedUtc TEXT NULL,
    AskedAgainOf TEXT NULL
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
CREATE TABLE ReviewReportCases (
    ReportId TEXT NOT NULL REFERENCES ReviewReports(ReportId) ON DELETE CASCADE,
    CaseKey TEXT NOT NULL,
    WordKey TEXT NOT NULL,
    Word TEXT NOT NULL,
    WritingSystemId TEXT,
    WordSort BLOB NOT NULL,
    WritingSystemSort BLOB NOT NULL,
    FactsJson TEXT NOT NULL,
    PRIMARY KEY (ReportId, CaseKey)
);
CREATE TABLE ReviewReportEvents (
    ReportId TEXT NOT NULL REFERENCES ReviewReports(ReportId) ON DELETE CASCADE,
    CaseKey TEXT NOT NULL,
    Ordinal INTEGER NOT NULL,
    FactsJson TEXT NOT NULL,
    PRIMARY KEY (ReportId, CaseKey, Ordinal)
);
CREATE TABLE ReviewReportMemberships (
    ReportId TEXT NOT NULL REFERENCES ReviewReports(ReportId) ON DELETE CASCADE,
    CaseKey TEXT NOT NULL,
    Ordinal INTEGER NOT NULL,
    SetId TEXT NOT NULL,
    Role TEXT NOT NULL,
    CellId TEXT,
    VariantId TEXT,
    FactsJson TEXT NOT NULL,
    PRIMARY KEY (ReportId, CaseKey, Ordinal)
);
CREATE TABLE ReviewReports (
    ReportId TEXT PRIMARY KEY,
    ProjectKey TEXT NOT NULL,
    ProposalId TEXT NOT NULL REFERENCES Proposals(ProposalId),
    TrialJobId TEXT NOT NULL,
    ManifestDigest TEXT NOT NULL,
    SummaryJson TEXT NOT NULL,
    SummaryDigest TEXT NOT NULL,
    InputReferencesJson TEXT NOT NULL,
    DetailAvailable INTEGER NOT NULL,
    DetailBytes INTEGER NOT NULL,
    CreatedUtc TEXT NOT NULL
);
CREATE TABLE SourceIndexState (
    SourceId TEXT COLLATE NOCASE NOT NULL,
    Kind TEXT NOT NULL,
    Generation TEXT NOT NULL,
    SourceDigest TEXT NOT NULL,
    IdentityJson TEXT NOT NULL,
    Status TEXT NOT NULL,
    TokenizerVersion TEXT NOT NULL,
    AnchorVersion TEXT NOT NULL,
    PRIMARY KEY (SourceId)
);
CREATE TABLE SourcePassages (
    SourceId TEXT COLLATE NOCASE NOT NULL,
    PassageId TEXT NOT NULL,
    Locator TEXT NOT NULL,
    ContentHash TEXT NOT NULL,
    TotalPlaces INTEGER NOT NULL,
    EligiblePlaces INTEGER NOT NULL,
    PRIMARY KEY (SourceId, PassageId),
    CHECK (TotalPlaces >= 0 AND EligiblePlaces BETWEEN 0 AND TotalPlaces)
);
CREATE TABLE SourceWordPlaces (
    SourceId TEXT COLLATE NOCASE NOT NULL,
    PassageId TEXT NOT NULL,
    Ordinal INTEGER NOT NULL,
    Anchor TEXT NOT NULL,
    Verse TEXT NOT NULL,
    Surface TEXT NOT NULL,
    WordformKey TEXT NOT NULL,
    ReadingKey TEXT,
    Signature TEXT,
    State TEXT NOT NULL,
    PRIMARY KEY (SourceId, PassageId, Ordinal),
    CHECK (Ordinal >= 0),
    UNIQUE (SourceId, PassageId, Anchor),
    FOREIGN KEY (SourceId, PassageId) REFERENCES SourcePassages (SourceId, PassageId),
    FOREIGN KEY (SourceId, WordformKey) REFERENCES SourceWordforms (SourceId, WordformKey)
);
CREATE TABLE SourceWordforms (
    SourceId TEXT COLLATE NOCASE NOT NULL,
    WordformKey TEXT NOT NULL,
    WritingSystem TEXT NOT NULL,
    Normalized TEXT NOT NULL,
    Display TEXT NOT NULL,
    FieldWorksId TEXT,
    PRIMARY KEY (SourceId, WordformKey)
);
CREATE TABLE StoreRelocations (
    RelocationId INTEGER PRIMARY KEY AUTOINCREMENT,
    OldFwDataPath TEXT NOT NULL,
    NewFwDataPath TEXT NOT NULL,
    RelocatedUtc TEXT NOT NULL,
    Basis TEXT NOT NULL CHECK (Basis IN ('project-guid', 'name'))
);
CREATE TABLE query_forms (Ordinal INTEGER PRIMARY KEY, RowJson TEXT NOT NULL);
CREATE TABLE query_objects (Ordinal INTEGER PRIMARY KEY, RowJson TEXT NOT NULL);
CREATE TABLE query_snapshot (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    TrialId TEXT NOT NULL,
    GrammarHash TEXT NOT NULL,
    BundleId TEXT NOT NULL,
    WritingSystem TEXT NOT NULL,
    GraphemesJson TEXT NOT NULL,
    UnresolvedVariants INTEGER NOT NULL,
    SourceWriteTicks INTEGER NOT NULL,
    TrialCreatedUtc TEXT NOT NULL,
    TrialQueueOrder REAL NOT NULL,
    CoverageJson TEXT NOT NULL
);
CREATE TABLE query_stems (Ordinal INTEGER PRIMARY KEY, RowJson TEXT NOT NULL);
CREATE TABLE query_uses (Ordinal INTEGER PRIMARY KEY, TargetKind TEXT NOT NULL, RowJson TEXT NOT NULL);
CREATE INDEX IX_AssessedWords_AnalysisComparisonJson ON AssessedWords(AnalysisComparisonJson) WHERE AnalysisComparisonJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_Assessment ON AssessedWords(AssessmentId);
CREATE INDEX IX_AssessedWords_CorrectnessJson ON AssessedWords(CorrectnessJson) WHERE CorrectnessJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_MissedApprovedJson ON AssessedWords(MissedApprovedJson) WHERE MissedApprovedJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_MorphologyJson ON AssessedWords(MorphologyJson) WHERE MorphologyJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_ReadingGradesJson ON AssessedWords(ReadingGradesJson) WHERE ReadingGradesJson IS NOT NULL;
CREATE INDEX IX_AssessedWords_Word ON AssessedWords(AssessmentId, Word);
CREATE INDEX IX_AssessmentWordSetBindings_Owner ON AssessmentWordSetBindings(OwnerAssessmentId);
CREATE INDEX IX_Assessments_Kind ON Assessments(Kind);
CREATE INDEX IX_Assessments_Proposal ON Assessments(ProposalId);
CREATE UNIQUE INDEX IX_GrammarChecks_Project ON GrammarChecks(BaselineToken) WHERE ProposalId IS NULL;
CREATE UNIQUE INDEX IX_GrammarChecks_Proposal
    ON GrammarChecks(BaselineToken, ProposalId, IntentDigest) WHERE ProposalId IS NOT NULL;
CREATE INDEX IX_Hypotheses_Seq ON Hypotheses(Seq);
CREATE INDEX IX_Hypotheses_Shape ON Hypotheses(
    ChangeText COLLATE NOCASE, Environment COLLATE NOCASE, HostClass COLLATE NOCASE, Slot COLLATE NOCASE);
CREATE INDEX IX_Hypotheses_StatusSeq ON Hypotheses(Status, Seq);
CREATE INDEX IX_HypothesisProposals_Proposal ON HypothesisProposals(ProposalId);
CREATE INDEX IX_IndexedCorpusDocuments_Shard ON IndexedCorpusDocuments (ShardId);
CREATE UNIQUE INDEX IX_IndexedCorpusOccurrences_Anchor ON IndexedCorpusOccurrences (ShardId, Segment, Word);
CREATE INDEX IX_IndexedCorpusOccurrences_Form ON IndexedCorpusOccurrences (FormId, ShardId, Ordinal);
CREATE INDEX IX_IndexedCorpusShards_Stage ON IndexedCorpusShards (StageGeneration);
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
CREATE INDEX IX_ProposalComments_Proposal ON ProposalComments(ProposalId, CommentId);
CREATE INDEX IX_ProposalDifferenceBuckets_Bucket ON ProposalDifferenceBuckets(DifferenceId, Bucket, CaseKey);
CREATE INDEX IX_ProposalDifferenceOccurrences_Order
    ON ProposalDifferenceOccurrences(DifferenceId, TextId, LineNumber, OccurrenceIndex);
CREATE UNIQUE INDEX IX_ProposalDifferences_ProposalText ON ProposalDifferences(ProposalId, TextId, Reference);
CREATE INDEX IX_ProposalPictures_Proposal ON ProposalPictures(ProposalId, PictureId);
CREATE INDEX IX_ProposalRationaleRevisions_Proposal ON ProposalRationaleRevisions(ProposalId, RevisionId);
CREATE UNIQUE INDEX IX_Proposals_DraftName ON Proposals(DraftName);
CREATE UNIQUE INDEX IX_Proposals_OpenRole ON Proposals(Role) WHERE DraftName IS NOT NULL;
CREATE INDEX IX_Requests_Status ON Requests(Status, CreatedUtc);
CREATE INDEX IX_RetainedInvocationMembers_Assessment ON RetainedInvocationMembers(AssessmentId);
CREATE INDEX IX_RetainedInvocations_Project ON RetainedInvocations(ProjectKey);
CREATE INDEX IX_ReviewReportCases_Order ON ReviewReportCases(ReportId, WordSort, WritingSystemSort, CaseKey);
CREATE INDEX IX_ReviewReportMemberships_Cell
    ON ReviewReportMemberships(ReportId, Role, SetId, CellId, CaseKey, Ordinal);
CREATE UNIQUE INDEX IX_ReviewReports_ProjectManifest ON ReviewReports(ProjectKey, ManifestDigest);
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
