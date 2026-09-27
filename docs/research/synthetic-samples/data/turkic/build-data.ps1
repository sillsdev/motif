$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../../')).Path
$rawRoot = Join-Path $repoRoot 'bin/data-raw/synthetic-turkic'
$unimorphPath = Join-Path $rawRoot 'unimorph-tur.tsv'
New-Item -ItemType Directory -Force -Path $rawRoot | Out-Null
$tatoebaFiles = Get-ChildItem -LiteralPath $rawRoot -File | Where-Object {
    $_.Name -match '^tatoeba-(tur-eng|page-[0-9]+|random-[0-9]+|sentence-[0-9]+)\.json$'
}

if (!(Test-Path -LiteralPath $unimorphPath)) {
    $unimorphUri = 'https://raw.githubusercontent.com/unimorph/tur/6c179ace7d2f3d7f3484020e5304c1544d07bb6b/tur'
    Invoke-WebRequest -Uri $unimorphUri -OutFile $unimorphPath
}
$unimorphRevision = '6c179ace7d2f3d7f3484020e5304c1544d07bb6b'
$wiktionaryRevisions = @{
    adam = 92703278
    ağaç = 92711112
    elma = 89733167
    gece = 92288803
    gün = 92176131
    kahve = 92944246
    kapı = 90798035
    kedi = 90290154
    köpek = 92174700
    masa = 92440253
    oda = 92423372
    okul = 92687100
    para = 92525889
    şehir = 91064158
    telefon = 92566785
    yol = 92421319
    açmak = 90126917
    almak = 92513394
    bilmek = 90858954
    çalışmak = 83931113
    gelmek = 92448097
    görmek = 92288214
    içmek = 92270350
    istemek = 90026093
    okumak = 92186118
    olmak = 92166434
    sevmek = 91576520
    uyumak = 92193373
    vermek = 91985070
    yapmak = 92284201
    yazmak = 84666697
}

$nounGlosses = @{
    adam = 'man'
    ağaç = 'tree'
    elma = 'apple'
    gece = 'night'
    gün = 'day'
    kahve = 'coffee'
    kapı = 'door'
    kedi = 'cat'
    köpek = 'dog'
    masa = 'table'
    oda = 'room'
    okul = 'school'
    para = 'money'
    şehir = 'city'
    telefon = 'telephone'
    yol = 'road; way'
}

$verbGlosses = @{
    açmak = 'open'
    almak = 'take; buy'
    bilmek = 'know'
    çalışmak = 'work'
    gelmek = 'come'
    görmek = 'see'
    içmek = 'drink'
    istemek = 'want'
    okumak = 'read; study'
    olmak = 'be; become'
    sevmek = 'love; like'
    uyumak = 'sleep'
    vermek = 'give'
    yapmak = 'do; make'
    yazmak = 'write'
}

$richNouns = @('adam', 'elma', 'gece', 'gün', 'kahve', 'kapı', 'kedi', 'masa', 'oda', 'okul', 'şehir', 'telefon', 'yol')
$sparseNouns = @('ağaç', 'köpek', 'para')
$nounTags = @(
    'N;NOM;SG',
    'N;NOM;PL',
    'N;ACC;SG',
    'N;DAT;SG',
    'N;LOC;SG',
    'N;ABL;SG',
    'N;GEN;SG',
    'N;NOM;SG;PSS1P',
    'N;LOC;SG;PSS1P',
    'N;NOM;SG;PSS3S',
    'N;ACC;SG;PSS3S',
    'N;DAT;SG;PSS3S',
    'N;LOC;SG;PSS3S',
    'N;ABL;SG;PSS3S',
    'N;GEN;SG;PSS3S',
    'N;NOM;PL;PSS3S',
    'N;LOC;PL;PSS3S'
)
$sparseNounTags = @('N;NOM;SG', 'N;NOM;PL', 'N;ACC;SG', 'N;DAT;SG', 'N;LOC;SG', 'N;ABL;SG', 'N;GEN;SG')
$verbTags = @(
    'V;NFIN',
    'V;IND;PRS;PROG;3;SG;POS;DECL',
    'V;IND;PRS;PROG;1;SG;POS;DECL',
    'V;IND;PST;3;SG;POS;DECL',
    'V;IND;PST;1;SG;POS;DECL'
)
$verbLemmas = @('açmak', 'almak', 'bilmek', 'çalışmak', 'gelmek', 'görmek', 'içmek', 'istemek', 'okumak', 'olmak', 'sevmek', 'uyumak', 'vermek', 'yapmak', 'yazmak')

$wantedKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($lemma in $richNouns) {
    foreach ($tag in $nounTags) {
        [void]$wantedKeys.Add("$lemma`t$tag")
    }
}
foreach ($lemma in $sparseNouns) {
    foreach ($tag in $sparseNounTags) {
        [void]$wantedKeys.Add("$lemma`t$tag")
    }
}
foreach ($lemma in $verbLemmas) {
    foreach ($tag in $verbTags) {
        [void]$wantedKeys.Add("$lemma`t$tag")
    }
}

$formsByKey = @{}
foreach ($line in [System.IO.File]::ReadLines($unimorphPath)) {
    $parts = $line.Split("`t")
    if ($parts.Length -lt 3) { continue }
    $key = "$($parts[0])`t$($parts[2])"
    if ($wantedKeys.Contains($key) -and !$formsByKey.ContainsKey($key)) {
        $formsByKey[$key] = $parts[1]
    }
}

function Get-LastVowel([string]$text) {
    $matches = [regex]::Matches($text.ToLowerInvariant(), '[aıoueiöü]')
    if ($matches.Count -eq 0) { return '' }
    return $matches[$matches.Count - 1].Value
}

function Get-HighVowel([string]$text) {
    switch (Get-LastVowel $text) {
        { $_ -in @('a', 'ı') } { return 'ı' }
        { $_ -in @('o', 'u') } { return 'u' }
        { $_ -in @('e', 'i') } { return 'i' }
        { $_ -in @('ö', 'ü') } { return 'ü' }
        default { return '' }
    }
}

function Get-Plural([string]$text) {
    if ((Get-LastVowel $text) -in @('a', 'ı', 'o', 'u')) { return 'lar' }
    return 'ler'
}

function Get-Poss3([string]$text) {
    $high = Get-HighVowel $text
    if ($text -match '[aıoueiöü]$') { return "s$high" }
    return $high
}

function Get-Poss1P([string]$text) {
    $high = Get-HighVowel $text
    if ($text -match '[aıoueiöü]$') { return "m${high}z" }
    return "${high}m${high}z"
}

function Get-Segmentation([string]$lemma, [string]$form, [string]$features, [string]$pos) {
    if ($pos -eq 'V') {
        $stem = $lemma.Substring(0, $lemma.Length - 3)
        if ($features -eq 'V;NFIN') { return "$stem + -$($lemma.Substring($lemma.Length - 3))" }
        if (!$form.StartsWith($stem, [System.StringComparison]::Ordinal)) { return '' }
        $tail = $form.Substring($stem.Length)
        if ($features -match ';PROG;1;SG;') {
            if ($form.Length -lt 2) { return '' }
            $person = $form.Substring($form.Length - 2)
            $tense = $form.Substring($stem.Length, $form.Length - $stem.Length - 2)
            return "$stem + -$tense + -$person"
        }
        if ($features -match ';PST;1;SG;') {
            if (!$form.EndsWith('m', [System.StringComparison]::Ordinal)) { return '' }
            $tense = $form.Substring($stem.Length, $form.Length - $stem.Length - 1)
            return "$stem + -$tense + -m"
        }
        if ($features -match ';PROG;3;SG;|;PST;3;SG;') { return "$stem + -$tail" }
        return ''
    }

    if ($features -eq 'N;NOM;SG') { return $lemma }
    if (!$form.StartsWith($lemma, [System.StringComparison]::Ordinal)) { return '' }
    $tail = $form.Substring($lemma.Length)

    if ($features -eq 'N;NOM;PL') { return "$lemma + -$tail" }
    if ($features -eq 'N;NOM;SG;PSS1P') {
        $poss = Get-Poss1P $lemma
        if ($tail -eq $poss) { return "$lemma + -$poss" }
    }
    if ($features -eq 'N;LOC;SG;PSS1P') {
        $poss = Get-Poss1P $lemma
        if ($tail.StartsWith($poss, [System.StringComparison]::Ordinal)) {
            return "$lemma + -$poss + -$($tail.Substring($poss.Length))"
        }
    }
    if ($features -match 'PSS3S') {
        $poss = Get-Poss3 $lemma
        if ($features -eq 'N;NOM;SG;PSS3S' -and $tail -eq $poss) { return "$lemma + -$poss" }
        if ($features -eq 'N;NOM;PL;PSS3S') {
            $plural = Get-Plural $lemma
            $pluralPoss = Get-Poss3 "$lemma$plural"
            if ($tail -eq "$plural$pluralPoss") { return "$lemma + -$plural + -$pluralPoss" }
        }
        if ($features -match 'N;LOC;PL;PSS3S') {
            $plural = Get-Plural $lemma
            $pluralPoss = Get-Poss3 "$lemma$plural"
            $expectedPrefix = "${plural}${pluralPoss}n"
            if ($tail.StartsWith($expectedPrefix, [System.StringComparison]::Ordinal)) {
                return "$lemma + -$plural + -$pluralPoss + -n + -$($tail.Substring($expectedPrefix.Length))"
            }
        }
        if ($features -match 'N;(ACC|DAT|LOC|ABL|GEN);SG;PSS3S' -and $tail.StartsWith($poss, [System.StringComparison]::Ordinal)) {
            $rest = $tail.Substring($poss.Length)
            if ($rest.StartsWith('n', [System.StringComparison]::Ordinal)) {
                return "$lemma + -$poss + -n + -$($rest.Substring(1))"
            }
        }
    }

    if ($features -match '^N;(ACC|DAT|LOC|ABL|GEN);SG$') {
        $case = ($features -split ';')[1]
        $buffer = ''
        if ($lemma -match '[aıoueiöü]$' -and $case -in @('ACC', 'DAT')) { $buffer = 'y' }
        if ($lemma -match '[aıoueiöü]$' -and $case -eq 'GEN') { $buffer = 'n' }
        if ($buffer -and $tail.StartsWith($buffer, [System.StringComparison]::Ordinal)) {
            return "$lemma + -$buffer + -$($tail.Substring(1))"
        }
        return "$lemma + -$tail"
    }
    return ''
}

$paradigmRows = [System.Collections.Generic.List[object]]::new()
foreach ($lemma in $richNouns) {
    foreach ($tag in $nounTags) {
        $key = "$lemma`t$tag"
        if (!$formsByKey.ContainsKey($key)) { continue }
        $form = [string]$formsByKey[$key]
        $sourceUrl = 'https://en.wiktionary.org/w/index.php?title={0}&oldid={1}' -f [uri]::EscapeDataString($lemma), $wiktionaryRevisions[$lemma]
        $paradigmRows.Add([pscustomobject]@{
            lemma = $lemma
            gloss = $nounGlosses[$lemma]
            pos = 'N'
            inflected_form = $form
            features = $tag
            segmentation = Get-Segmentation $lemma $form $tag 'N'
            source = "UniMorph tur @ $unimorphRevision; Wiktionary $sourceUrl"
        })
    }
}
foreach ($lemma in $sparseNouns) {
    foreach ($tag in $sparseNounTags) {
        $key = "$lemma`t$tag"
        if (!$formsByKey.ContainsKey($key)) { continue }
        $form = [string]$formsByKey[$key]
        $sourceUrl = 'https://en.wiktionary.org/w/index.php?title={0}&oldid={1}' -f [uri]::EscapeDataString($lemma), $wiktionaryRevisions[$lemma]
        $paradigmRows.Add([pscustomobject]@{
            lemma = $lemma
            gloss = $nounGlosses[$lemma]
            pos = 'N'
            inflected_form = $form
            features = $tag
            segmentation = Get-Segmentation $lemma $form $tag 'N'
            source = "UniMorph tur @ $unimorphRevision; Wiktionary $sourceUrl"
        })
    }
}
foreach ($lemma in $verbLemmas) {
    foreach ($tag in $verbTags) {
        $key = "$lemma`t$tag"
        if (!$formsByKey.ContainsKey($key)) { continue }
        $form = [string]$formsByKey[$key]
        $sourceUrl = 'https://en.wiktionary.org/w/index.php?title={0}&oldid={1}' -f [uri]::EscapeDataString($lemma), $wiktionaryRevisions[$lemma]
        $paradigmRows.Add([pscustomobject]@{
            lemma = $lemma
            gloss = $verbGlosses[$lemma]
            pos = 'V'
            inflected_form = $form
            features = $tag
            segmentation = Get-Segmentation $lemma $form $tag 'V'
            source = "UniMorph tur @ $unimorphRevision; Wiktionary $sourceUrl"
        })
    }
}

$morphemeRows = @(
    [pscustomobject]@{ morpheme_id = 'N.STEM'; form = 'lexical stem'; allomorphs = 'lemma-specific'; gloss = 'lexical base'; category = 'stem'; slot_or_position = 'noun base'; conditioning_environment = 'precedes the noun suffix sequence'; source = 'YAE2019 §2.2; §4.2 Table 12; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.PL'; form = '-lAr'; allomorphs = '-lar, -ler'; gloss = 'plural'; category = 'suffix'; slot_or_position = 'noun slot 1'; conditioning_environment = 'A is a after a back vowel and e after a front vowel'; source = 'YAE2019 §2.1 Table 13; §4.2 Table 12; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.PSS.1PL'; form = '-(I)mIz'; allomorphs = '-ımız, -imiz, -umuz, -ümüz; -mız, -miz, -muz, -müz'; gloss = 'possessive first-person plural'; category = 'suffix'; slot_or_position = 'noun slot 2'; conditioning_environment = 'the first I occurs after a consonant and is absent after a vowel; final I follows four-way harmony; follows plural'; source = 'YAE2019 §2.1 Table 13; §4.2 Table 12; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.PSS.3SG'; form = '-(s)I'; allomorphs = '-ı, -i, -u, -ü; -sı, -si, -su, -sü'; gloss = 'possessive third-person singular'; category = 'suffix'; slot_or_position = 'noun slot 2'; conditioning_environment = 'I follows four-way harmony; s appears after a vowel-final stem; precedes case'; source = 'YAE2019 §2.1 Table 13; §4.2 Table 12; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.CASE.ACC'; form = '-(y)I'; allomorphs = '-ı, -i, -u, -ü; -yı, -yi, -yu, -yü'; gloss = 'accusative'; category = 'suffix'; slot_or_position = 'noun slot 3'; conditioning_environment = 'I follows four-way harmony; y links vowel-final unpossessed stems'; source = 'YAE2019 §2.1 Table 13; §2.2; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.CASE.DAT'; form = '-(y)A'; allomorphs = '-a, -e; -ya, -ye'; gloss = 'dative'; category = 'suffix'; slot_or_position = 'noun slot 3'; conditioning_environment = 'A follows two-way harmony; y links vowel-final unpossessed stems'; source = 'YAE2019 §2.1 Table 13; §4.3; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.CASE.LOC'; form = '-DA'; allomorphs = '-da, -de, -ta, -te'; gloss = 'locative'; category = 'suffix'; slot_or_position = 'noun slot 3'; conditioning_environment = 'A follows two-way harmony; D is t after a voiceless consonant, otherwise d'; source = 'YAE2019 §2.1 Table 13; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.CASE.ABL'; form = '-DAn'; allomorphs = '-dan, -den, -tan, -ten'; gloss = 'ablative'; category = 'suffix'; slot_or_position = 'noun slot 3'; conditioning_environment = 'A follows two-way harmony; D is t after a voiceless consonant, otherwise d'; source = 'YAE2019 §2.1 Table 13; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.CASE.GEN'; form = '-(n)In'; allomorphs = '-ın, -in, -un, -ün; -nın, -nin, -nun, -nün'; gloss = 'genitive'; category = 'suffix'; slot_or_position = 'noun slot 3'; conditioning_environment = 'I follows four-way harmony; n links vowel-final unpossessed stems'; source = 'YAE2019 §2.1 Table 13; §4.3; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.CASE.INST'; form = '-(y)lA'; allomorphs = '-la, -le; -yla, -yle'; gloss = 'instrumental/with'; category = 'suffix'; slot_or_position = 'noun slot 3'; conditioning_environment = 'A follows two-way harmony; y links a vowel-final unpossessed stem'; source = 'YAE2019 §2.1 Table 13; §2.2' }
    [pscustomobject]@{ morpheme_id = 'N.BUF.Y'; form = 'y'; allomorphs = 'y'; gloss = 'linking consonant'; category = 'infix'; slot_or_position = 'before vowel-initial case suffix'; conditioning_environment = 'after a vowel-final unpossessed noun stem with accusative or dative'; source = 'YAE2019 §2.1 Table 3; §4.3; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'N.BUF.N'; form = 'n'; allomorphs = 'n'; gloss = 'linking consonant'; category = 'infix'; slot_or_position = 'between third-person possessive and case'; conditioning_environment = 'after third-person possessive before case suffix'; source = 'YAE2019 §4.2 Table 12; §4.3; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'V.INF'; form = '-mAk'; allomorphs = '-mak, -mek'; gloss = 'infinitive'; category = 'suffix'; slot_or_position = 'verb citation form'; conditioning_environment = 'A follows two-way harmony'; source = 'YAE2019 §2; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'V.PROG'; form = '-(I)yor'; allomorphs = '-ıyor, -iyor, -uyor, -üyor; -yor after some vowel-final stems'; gloss = 'progressive'; category = 'suffix'; slot_or_position = 'verb stem before person marking'; conditioning_environment = 'I follows four-way harmony; some vowel-final and a/e-final stems show stem alternation'; source = 'YAE2019 §2; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'V.PST'; form = '-DI'; allomorphs = '-dı, -di, -du, -dü, -tı, -ti, -tu, -tü'; gloss = 'past'; category = 'suffix'; slot_or_position = 'verb stem before person marking'; conditioning_environment = 'I follows four-way harmony; D becomes t after a voiceless consonant'; source = 'YAE2019 §2; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'V.AGR.1SG'; form = '-(I)m / -m'; allomorphs = '-ım, -im, -um, -üm; -m'; gloss = 'first-person singular agreement'; category = 'suffix'; slot_or_position = 'after finite tense/aspect'; conditioning_environment = 'high-vowel agreement follows four-way harmony; past first-person singular uses -m'; source = 'YAE2019 §2; UM Turkish' }
    [pscustomobject]@{ morpheme_id = 'V.AGR.3SG'; form = 'Ø'; allomorphs = 'zero'; gloss = 'third-person singular agreement'; category = 'suffix'; slot_or_position = 'finite verb ending'; conditioning_environment = 'no overt marker in the selected indicative forms'; source = 'YAE2019 §2; UM Turkish' }
)

$sentenceIds = @(
    1280708, 1285396, 1294472, 1351651, 13717134, 13764684, 13767251, 13782728,
    13784370, 13787701, 13789105, 13814085, 13814195, 13863092, 13899382, 13899404,
    13899405, 13899608, 13911714, 13912180, 13912586, 13932689, 13932926, 13933156,
    13942938, 13962753, 14020476, 1538621, 1612034, 1620177, 2084739, 2116339, 2714563,
    2763009, 3799449, 3873489, 3879448, 4096493, 4157814, 4202170, 4446406, 4525282,
    4725798, 4751157, 4755628, 4826063, 4849714, 4929148, 5036621, 5161673, 5531720,
    5666904, 5787028, 5863678, 5957244, 6086349, 6102085, 6302239, 6394544, 6554981,
    6716397, 6763721, 6853640, 7124591, 12062343, 13979488, 13862684, 13740397
)

$sentenceById = @{}
foreach ($file in $tatoebaFiles) {
    $page = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    foreach ($sentence in $page.data) {
        if (!$sentenceById.ContainsKey([string]$sentence.id)) { $sentenceById[[string]$sentence.id] = $sentence }
    }
}

$sentenceRows = [System.Collections.Generic.List[object]]::new()
$paradigmWords = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($row in $paradigmRows) {
    [void]$paradigmWords.Add([string]$row.lemma)
    [void]$paradigmWords.Add([string]$row.inflected_form)
}
foreach ($id in $sentenceIds) {
    $key = [string]$id
    if (!$sentenceById.ContainsKey($key)) {
        $uri = "https://api.tatoeba.org/v1/sentences/$id`?showtrans=all"
        $response = Invoke-RestMethod -Uri $uri
        $cachePath = Join-Path $rawRoot "tatoeba-sentence-$id.json"
        $cacheJson = ConvertTo-Json -InputObject $response -Depth 12
        [System.IO.File]::WriteAllText($cachePath, $cacheJson, [System.Text.UTF8Encoding]::new($false))
        $sentenceById[$key] = $response.data
    }
    $sentence = $sentenceById[$key]
    $english = $sentence.translations |
        Where-Object { $_.lang -eq 'eng' -and $_.is_direct -and !$_.is_unapproved -and $_.license -eq 'CC BY 2.0 FR' -and $_.owner } |
        Sort-Object id |
        Select-Object -First 1
    if (!$english -or $sentence.license -ne 'CC BY 2.0 FR' -or $sentence.is_unapproved) {
        throw "Tatoeba sentence $id lacks an approved, directly linked CC BY 2.0 FR English pair"
    }
    $tokens = @([regex]::Matches($sentence.text, '[\p{L}\p{M}]+') | ForEach-Object { $_.Value })
    if ($tokens.Count -lt 2 -or $tokens.Count -gt 8) {
        throw "Tatoeba sentence $id is outside the 2-8 word selection range"
    }
    $hasParadigmWord = $false
    foreach ($token in $tokens) {
        if ($paradigmWords.Contains($token)) {
            $hasParadigmWord = $true
            break
        }
    }
    if (!$hasParadigmWord) {
        throw "Tatoeba sentence $id has no exact lemma or surface form from paradigms.tsv"
    }
    $source = 'Tatoeba #{0} ({1}); English #{2} ({3}); https://tatoeba.org/en/sentences/{0}; https://tatoeba.org/en/sentences/{2}' -f $sentence.id, $sentence.owner, $english.id, $english.owner
    $sentenceRows.Add([pscustomobject]@{
        id = "tatoeba-$($sentence.id)"
        sentence = $sentence.text
        english = $english.text
        gloss_or_features = ''
        source = $source
        licence = 'CC BY 2.0 FR'
    })
}

if ($paradigmRows.Count -lt 150 -or $paradigmRows.Count -gt 400) {
    throw "Expected 150-400 paradigm rows, selected $($paradigmRows.Count)"
}
if ($sentenceRows.Count -lt 60 -or $sentenceRows.Count -gt 150) {
    throw "Expected 60-150 sentence rows, selected $($sentenceRows.Count)"
}

function Write-Tsv([string]$path, [string[]]$columns, [object[]]$rows) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add(($columns -join "`t"))
    foreach ($row in $rows) {
        $cells = foreach ($column in $columns) {
            ([string]$row.$column) -replace '[\r\n\t]', ' '
        }
        $lines.Add(($cells -join "`t"))
    }
    [System.IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
}

Write-Tsv (Join-Path $PSScriptRoot 'morphemes.tsv') @('morpheme_id', 'form', 'allomorphs', 'gloss', 'category', 'slot_or_position', 'conditioning_environment', 'source') $morphemeRows
Write-Tsv (Join-Path $PSScriptRoot 'paradigms.tsv') @('lemma', 'gloss', 'pos', 'inflected_form', 'features', 'segmentation', 'source') $paradigmRows
Write-Tsv (Join-Path $PSScriptRoot 'sentences.tsv') @('id', 'sentence', 'english', 'gloss_or_features', 'source', 'licence') $sentenceRows

Write-Output "Wrote $($morphemeRows.Count) morphemes, $($paradigmRows.Count) paradigm rows, and $($sentenceRows.Count) sentence pairs."
