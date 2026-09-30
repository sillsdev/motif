import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { access, mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { syncSiteContent } from '../scripts/sync-core.mjs';

const execFileAsync = promisify(execFile);
const repositoryRoot = fileURLToPath(new URL('../../', import.meta.url));

async function markdownFiles(root) {
	const files = [];
	for (const entry of await readdir(root, { withFileTypes: true })) {
		const fullPath = path.join(root, entry.name);
		if (entry.isDirectory()) files.push(...await markdownFiles(fullPath));
		else if (entry.isFile() && entry.name.endsWith('.md')) files.push(fullPath);
	}
	return files;
}

async function builtCliPath() {
	if (process.env.MOTIF_SITE_CLI_EXE) {
		const explicitPath = path.resolve(process.env.MOTIF_SITE_CLI_EXE);
		await access(explicitPath);
		return explicitPath;
	}
	const root = path.join(repositoryRoot, 'bin');
	const executable = process.platform === 'win32' ? 'motif.exe' : 'motif';
	const configurations = await readdir(root, { withFileTypes: true }).catch((error) => {
		if (error.code === 'ENOENT') return [];
		throw error;
	});
	const candidates = configurations.filter((entry) => entry.isDirectory()).map((entry) => path.join(root, entry.name, executable));
	const available = [];
	for (const candidate of candidates) {
		try {
			await access(candidate);
			available.push(candidate);
		} catch (error) {
			if (error.code !== 'ENOENT') throw error;
		}
	}
	if (available.length > 1) throw new Error('Multiple Motif CLI apphosts found; set MOTIF_SITE_CLI_EXE.');
	return available[0] ?? null;
}

test('sync builds help, Walkthrough, API, and Developer pages from their source files', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-site-'));
	t.after(() => rm(root, { recursive: true, force: true }));

	const repository = path.join(root, 'repo');
	const site = path.join(repository, 'site');
	const help = path.join(repository, 'help', 'en');
	const walks = path.join(root, 'walks');
	const docs = path.join(repository, 'docs');
	const samples = path.join(repository, 'samples');
	const sampleBuild = path.join(root, 'sample-build');
	const apiXml = path.join(root, 'SIL.Motif.Contract.xml');

	await mkdir(path.join(help, 'commands'), { recursive: true });
	await mkdir(path.join(help, 'terms'), { recursive: true });
	await mkdir(path.join(help, 'guide', 'agents'), { recursive: true });
	await mkdir(path.join(help, 'guide', 'learn'), { recursive: true });
	await mkdir(path.join(docs, 'adr'), { recursive: true });
	await mkdir(path.join(samples, 'synthetic-turkic'), { recursive: true });
	await mkdir(sampleBuild, { recursive: true });
	await mkdir(path.join(walks, 'open-project'), { recursive: true });
	await mkdir(path.join(walks, 'open-project', 'steps'), { recursive: true });
	await writeFile(
		path.join(repository, 'help-export.json'),
		JSON.stringify({
			locale: 'en',
			siteRoot: 'https://motif-docs.pages.dev',
			entries: [
				{ kind: 'guide', code: 'agents/start-here', slug: 'agents/start-here', title: 'Agent guide', description: 'Start an agent workflow.', helpPage: null },
				{ kind: 'guide', code: 'learn/index', slug: 'learn/index', title: 'Learn', description: 'Choose a lesson.', helpPage: null },
				{ kind: 'guide', code: 'learn/stems-and-the-lexicon', slug: 'learn/stems-and-the-lexicon', title: 'Stems', description: 'Understand the lexicon.', helpPage: null },
				{
					kind: 'command',
					code: 'open project',
					slug: 'open-project',
					title: 'Open a project',
					description: 'Loads a language project into Motif.',
					helpPage: null,
					usage: ['Usage: motif open project'],
					surface: 'Released',
					url: 'https://motif-docs.pages.dev/reference/commands/open-project/',
				},
				{
					kind: 'term',
					code: 'proposal',
					slug: 'proposal',
					title: 'Proposal',
					description: 'A named set of changes for one project.',
					helpPage: null,
					url: 'https://motif-docs.pages.dev/reference/terms/proposal/',
				},
			],
		}),
	);
	await writeFile(
		path.join(help, 'commands', 'open-project.md'),
		'# Open a project\n\nLearn about [Proposal](term:proposal), [the agent guide](guide:agents/start-here), [lessons](guide:learn/index), and [stems](guide:learn/stems-and-the-lexicon).\n\n![Project view](shot:open-project/overview)\n',
	);
	await writeFile(path.join(help, 'terms', 'proposal.md'), '# Proposal\n\nA named set of changes.\n');
	await writeFile(path.join(help, 'guide', 'what-is-motif.md'), '# What Motif does\n\nMeasure a grammar and try changes safely.\n\n![Picture coming later](shot:not-built/step-one)\n');
	await writeFile(path.join(help, 'guide', 'open-a-project.md'), '# Opening a project\n\nOpen a project with [open project](cmd:open%20project).\n');
	await writeFile(path.join(help, 'guide', 'agents', 'start-here.md'), '# Using Motif from an agent\n\nStart with [Proposal](term:proposal).\n');
	await writeFile(path.join(help, 'guide', 'learn', 'synthetic-turkic-plural-harmony.md'), '# Fix vowel harmony\n\nRead the failing word with [Try a Word](cmd:open%20project).\n');
	const orderedLearnLessons = [
		'what-the-parser-knows',
		'stems-and-the-lexicon',
		'affixes-slots-and-templates',
		'allomorphs-and-environments',
		'phonological-rules',
		'reading-why-a-word-fails',
		'why-a-grammar-is-slow',
		'working-with-an-ai-consultant',
	];
	for (const slug of orderedLearnLessons) {
		await writeFile(path.join(help, 'guide', 'learn', `${slug}.md`), `# ${slug}\n\nLesson content.\n`);
	}
	await writeFile(path.join(samples, 'synthetic-turkic', 'sample.json'), JSON.stringify({
		id: 'synthetic-turkic',
		title: 'Synthetic Turkic-style sample',
		language: { name: 'Turkish', tag: 'tr' },
		teaches: ['suffix slots in order', 'vowel harmony'],
		summary: 'Practice measuring and repairing a small teaching grammar.',
		disclaimer: 'Generated to demonstrate Motif. Modelled loosely on Turkish; not real Turkish data and not a description of any language.',
	}));
	await writeFile(path.join(sampleBuild, 'synthetic-turkic-fixed.fwbackup'), 'fixed-project');
	await writeFile(path.join(docs, 'adr', '0001-example.md'), '---\nlayout: old\n---\n\n# Example decision\n\n<!-- remove this -->\n\n[Design](../design.md#rules)\n');
	await writeFile(path.join(docs, 'design.md'), '# Design\n\n## Rules\n\nSee [the decision](adr/0001-example.md).\n');
	await writeFile(path.join(docs, 'issues.md'), '# Issues\n\nIssue records belong to the Developer section.\n');
	await writeFile(path.join(walks, 'open-project', 'steps', '01-overview.png'), 'image');
	await writeFile(path.join(walks, 'open-project', 'steps', '01-overview-annotated.png'), 'annotated');
	await writeFile(path.join(walks, 'open-project', 'captions.en.vtt'), 'WEBVTT\n');
	await writeFile(path.join(walks, 'open-project', 'clip.webm'), 'video');
	await writeFile(path.join(walks, 'open-project', 'clip.mp4'), 'video');
	await writeFile(path.join(walks, 'open-project', 'clip.webp'), 'image');
	await writeFile(
		path.join(walks, 'open-project', 'manifest.json'),
		JSON.stringify({
			id: 'open-project',
			locale: 'en',
			title: 'Open a project',
			description: 'Open a project and reach its Overview.',
			width: 1280,
			height: 720,
			fps: 30,
			steps: [
				{
					id: 'overview',
					caption: 'View the project Overview.',
					startMs: 0,
					endMs: 3000,
					screenshot: 'steps/01-overview.png',
					annotated: 'steps/01-overview-annotated.png',
					callouts: [{ x: 10, y: 10, width: 20, height: 20, label: '1' }],
				},
			],
			clip: { webm: 'clip.webm', mp4: 'clip.mp4', webp: 'clip.webp', poster: 'steps/01-overview.png' },
		}),
	);
	await writeFile(apiXml, '<doc><members><member name="T:SIL.Motif.Contract.MotifJson"><summary>JSON conventions.</summary></member><member name="M:SIL.Motif.Contract.MotifJson.CreateOptions"><summary>Creates fresh options.</summary></member></members></doc>');

	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });

	const command = await readFile(path.join(site, 'src', 'content', 'docs', 'reference', 'commands', 'open-project.md'), 'utf8');
	const guideIndex = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'index.md'), 'utf8');
	const guideStart = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'agents', 'start-here.md'), 'utf8');
	const guideWhat = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'what-is-motif.md'), 'utf8');
	const design = await readFile(path.join(site, 'src', 'content', 'docs', 'developers', 'design.md'), 'utf8');
	const issueDoc = await readFile(path.join(site, 'src', 'content', 'docs', 'developers', 'issues.md'), 'utf8');
	const api = await readFile(path.join(site, 'src', 'content', 'docs', 'reference', 'api', 'index.md'), 'utf8');
	const walkthroughPage = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'walkthroughs', 'open-project.mdx'), 'utf8');
	const learnPage = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'synthetic-turkic-plural-harmony.md'), 'utf8');
	const learnIndex = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	const samplesPage = await readFile(path.join(site, 'src', 'content', 'docs', 'samples', 'index.md'), 'utf8');

	assert.match(command, /\/reference\/terms\/proposal\//);
	assert.match(command, /\/walkthroughs\/open-project\/steps\/01-overview\.png/);
	assert.ok(command.includes('[the agent guide](/guide/agents/start-here/)'));
	assert.ok(command.includes('[lessons](/learn/)'));
	assert.ok(command.includes('[stems](/learn/stems-and-the-lexicon/)'));
	assert.match(command, /Surface: Released/);
	assert.match(command, /Usage: motif open project/);
	assert.doesNotMatch(command, /^# Open a project$/m);
	assert.match(guideIndex, /What Motif does[\s\S]*Opening a project[\s\S]*Using Motif from an agent/);
	assert.match(guideStart, /\/reference\/terms\/proposal\//);
	assert.doesNotMatch(guideStart, /^# Using Motif from an agent$/m);
	assert.match(guideWhat, /Picture coming later/);
	assert.doesNotMatch(guideWhat, /shot:not-built/);
	await assert.rejects(access(path.join(site, 'src', 'content', 'docs', 'developers', 'adr')));
	assert.match(design, /https:\/\/github\.com\/sillsdev\/motif\/blob\/main\/docs\/adr\/0001-example\.md/);
	assert.match(issueDoc, /Issue records belong to the Developer section\./);
	assert.match(api, /## `SIL\.Motif\.Contract\.MotifJson`/);
	assert.match(api, /### `CreateOptions`/);
	assert.match(api, /Creates fresh options\./);
	assert.match(walkthroughPage, /<Walkthrough/);
	assert.equal(await readFile(path.join(site, 'public', 'walkthroughs', 'open-project', 'clip.webm'), 'utf8'), 'video');
	assert.match(learnPage, /Fix vowel harmony/);
	assert.match(learnPage, /\/reference\/commands\/open-project\//);
	const learnSlugs = [...learnIndex.matchAll(/\(\/learn\/([^/]+)\/\)/g)].map((match) => match[1]);
	assert.deepEqual(learnSlugs, [...orderedLearnLessons, 'synthetic-turkic-plural-harmony']);
	assert.match(await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8'), /sidebar:\n  order: 0/);
	for (const [index, slug] of orderedLearnLessons.entries()) {
		const orderedPage = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', `${slug}.md`), 'utf8');
		assert.match(orderedPage, new RegExp(`sidebar:\\n  order: ${index + 1}\\n`));
	}
	assert.match(samplesPage, /Synthetic Turkic-style sample/);
	assert.match(samplesPage, /suffix slots in order, vowel harmony/);
	assert.match(samplesPage, /Generated to demonstrate Motif/);
	assert.match(samplesPage, /\/downloads\/samples\/synthetic-turkic-fixed\.fwbackup/);
	assert.match(samplesPage, /Broken project \(\.fwbackup\) is available in release builds/);
	assert.match(samplesPage, /id="synthetic-turkic"/);
	assert.doesNotMatch(samplesPage, /Includes a speed lesson/);
	assert.equal(await readFile(path.join(site, 'public', 'downloads', 'samples', 'synthetic-turkic-fixed.fwbackup'), 'utf8'), 'fixed-project');
	await assert.rejects(readFile(path.join(site, 'public', 'downloads', 'samples', 'synthetic-turkic-broken.fwbackup'), 'utf8'), { code: 'ENOENT' });
	const initialHomeSample = JSON.parse(await readFile(path.join(site, 'src', 'data', 'samples.json'), 'utf8'))[0];
	assert.equal(initialHomeSample.downloads.fixed, '/downloads/samples/synthetic-turkic-fixed.fwbackup');
	assert.equal(initialHomeSample.downloads.broken, null);
	assert.equal(initialHomeSample.lessonsHref, '/learn/synthetic-turkic-plural-harmony/');
	assert.equal(initialHomeSample.speedLesson, false);
	assert.equal(initialHomeSample.speedLessonHref, null);
	await assert.rejects(readFile(path.join(site, 'src', 'data', 'synthetic-turkic-performance.json'), 'utf8'), { code: 'ENOENT' });

	await writeFile(path.join(samples, 'synthetic-turkic', 'expected.json'), JSON.stringify({
		fixed: { words: 2, parsed: 2, textCoverage: 1 },
		broken: { words: 2, parsed: 1, textCoverage: 0.5, failing: { 'evler': 'plural-harmony' } },
	}));
	await writeFile(path.join(sampleBuild, 'synthetic-turkic-broken.fwbackup'), 'broken-project');
	await writeFile(path.join(help, 'guide', 'learn', 'synthetic-turkic-speed-benchmark.md'), '# Compare parsing before and after a fix\n\nThis lesson measures parse results before and after repairing the sample.\n');
	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });
	const learnIndexWithSpeed = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	assert.deepEqual(
		[...learnIndexWithSpeed.matchAll(/\(\/learn\/([^/]+)\/\)/g)].map((match) => match[1]),
		[...orderedLearnLessons, 'synthetic-turkic-plural-harmony', 'synthetic-turkic-speed-benchmark'],
	);
	assert.match(await readFile(path.join(site, 'src', 'content', 'docs', 'samples', 'index.md'), 'utf8'), /Includes a speed lesson/);
	assert.equal(await readFile(path.join(site, 'public', 'downloads', 'samples', 'synthetic-turkic-broken.fwbackup'), 'utf8'), 'broken-project');
	const homeSample = JSON.parse(await readFile(path.join(site, 'src', 'data', 'samples.json'), 'utf8'))[0];
	assert.deepEqual(homeSample.speedLesson, true);
	assert.deepEqual(homeSample.speedLessonHref, '/learn/synthetic-turkic-speed-benchmark/');
	assert.deepEqual(homeSample.lessonsHref, '/learn/synthetic-turkic-plural-harmony/');
	assert.deepEqual(homeSample.teaches, ['suffix slots in order', 'vowel harmony']);
	assert.deepEqual(homeSample.downloads, {
		fixed: '/downloads/samples/synthetic-turkic-fixed.fwbackup',
		broken: '/downloads/samples/synthetic-turkic-broken.fwbackup',
	});
	assert.deepEqual(JSON.parse(await readFile(path.join(site, 'src', 'data', 'synthetic-turkic-performance.json'), 'utf8')), {
		fixed: { words: 2, parsed: 2, textCoverage: 1 },
		broken: { words: 2, parsed: 1, textCoverage: 0.5 },
	});

	await rm(path.join(help, 'guide', 'learn'), { recursive: true, force: true });
	await rm(path.join(samples, 'synthetic-turkic', 'expected.json'));
	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });
	await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	await assert.rejects(readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'synthetic-turkic-plural-harmony.md'), 'utf8'), { code: 'ENOENT' });
	await assert.rejects(readFile(path.join(site, 'src', 'data', 'synthetic-turkic-performance.json'), 'utf8'), { code: 'ENOENT' });
	await writeFile(path.join(help, 'guide', 'unlisted-page.md'), '# Unlisted page\n\nThis page must not be silently dropped.\n');
	await assert.rejects(
		syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild }),
		/Guide page is missing from the published outline: unlisted-page/,
	);
});

test('sync publishes every authored Guide page from the built CLI export', async (t) => {
	const cli = await builtCliPath();
	if (!cli) {
		t.skip('Set MOTIF_SITE_CLI_EXE or build one Motif CLI apphost to run the real-source site sync check.');
		return;
	}
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-site-real-'));
	t.after(() => rm(root, { recursive: true, force: true }));

	const helpExportPath = path.join(root, 'help-export.json');
	const { stdout } = await execFileAsync(cli, ['help', '--all', '--json'], {
		encoding: 'utf8',
		maxBuffer: 16 * 1024 * 1024,
		windowsHide: true,
	});
	const helpExport = JSON.parse(stdout);
	assert.equal(helpExport.locale, 'en');
	assert.ok(Array.isArray(helpExport.entries));
	assert.ok(helpExport.entries.length > 0);
	await writeFile(helpExportPath, stdout);

	const site = path.join(root, 'site');
	const sourceGuideRoot = path.join(repositoryRoot, 'help', 'en', 'guide');
	const fixtureRoot = path.join(repositoryRoot, 'site', 'fixtures');
	await syncSiteContent({
		repository: repositoryRoot,
		site,
		helpExportPath,
		helpRoot: path.join(repositoryRoot, 'help'),
		walkthroughRoot: path.join(fixtureRoot, 'walkthroughs'),
		docsRoot: path.join(repositoryRoot, 'docs'),
		apiXmlPath: path.join(fixtureRoot, 'SIL.Motif.Contract.xml'),
		samplesRoot: path.join(fixtureRoot, 'samples'),
		samplesOut: path.join(fixtureRoot, 'samples'),
	});

	const sourcePages = (await markdownFiles(sourceGuideRoot))
		.map((file) => path.relative(sourceGuideRoot, file).replaceAll('\\', '/'))
		.filter((file) => !file.startsWith('learn/'));
	const publishedGuideRoot = path.join(site, 'src', 'content', 'docs', 'guide');
	const publishedPages = (await markdownFiles(publishedGuideRoot))
		.map((file) => path.relative(publishedGuideRoot, file).replaceAll('\\', '/'))
		.filter((file) => file !== 'index.md')
		.sort();
	assert.deepEqual(publishedPages, sourcePages.sort());

	const guideIndex = await readFile(path.join(publishedGuideRoot, 'index.md'), 'utf8');
	const pangloss = await readFile(path.join(publishedGuideRoot, 'pangloss.md'), 'utf8');
	const sourcePangloss = await readFile(path.join(sourceGuideRoot, 'pangloss.md'), 'utf8');
	const sourceTitle = sourcePangloss.match(/^#\s+([^\r\n]+)$/m)?.[1].trim();
	assert.ok(sourceTitle);
	assert.ok(guideIndex.includes(`[${sourceTitle}](/guide/pangloss/)`));
	assert.ok(pangloss.startsWith(`---\ntitle: ${JSON.stringify(sourceTitle)}\n`));
});
