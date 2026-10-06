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
const guideSections = [
	['what-is-motif', 'install', 'open-a-project', 'first-run-setup', 'reading-the-overview'],
	['overview', 'texts', 'keyboard-shortcuts', 'settings', 'try-a-word', 'timing', 'warnings', 'review-changes', 'ai-handoff'],
	['refresh-numbers', 'change-an-analysis', 'replace-a-pending-change', 'apply-to-fieldworks', 'when-a-change-no-longer-fits', 'switch-projects', 'cancel-a-long-run', 'when-something-goes-wrong'],
	['baseline', 'assessment', 'pangloss', 'text-coverage', 'default-selection', 'drift', 'pending-changes'],
	['agents/start-here', 'agents/output-and-exit-codes', 'agents/measure-a-grammar', 'agents/work-with-jobs', 'agents/handoff'],
];
const learnCodes = [
	'learn/index',
	'learn/what-the-parser-knows',
	'learn/stems-and-the-lexicon',
	'learn/affixes-slots-and-templates',
	'learn/allomorphs-and-environments',
	'learn/phonological-rules',
	'learn/modelling-a-grammar-the-parser-can-use',
	'learn/reading-why-a-word-fails',
	'learn/why-a-grammar-is-slow',
	'learn/working-with-an-ai-consultant',
	'learn/synthetic-turkic-plural-harmony',
];
const guideCodes = [...guideSections.flat(), ...learnCodes];

function guideEntry(code, overrides = {}) {
	const title = overrides.title ?? code;
	const route = code === 'learn/index'
		? '/learn/'
		: code.startsWith('learn/') ? `/${code.slice('learn/'.length) === 'index' ? 'learn' : `learn/${code.slice('learn/'.length)}`}/`
			: `/guide/${code}/`;
	return {
		kind: 'guide',
		code,
		slug: code,
		title,
		description: overrides.description ?? `Description for ${code}.`,
		helpPage: overrides.helpPage ?? `# ${title}\n\nContent for ${code}.\n`,
		url: `https://motif-docs.pages.dev${route}`,
	};
}

function fixtureGuideEntries(overrides = {}) {
	return guideCodes.map((code) => guideEntry(code, overrides[code]));
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

test('sync builds catalog, Walkthrough, API, and Developer pages', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-site-'));
	t.after(() => rm(root, { recursive: true, force: true }));

	const repository = path.join(root, 'repo');
	const site = path.join(repository, 'site');
	const helpRoot = path.join(repository, 'help');
	const help = path.join(helpRoot, 'en');
	const walks = path.join(root, 'walks');
	const docs = path.join(repository, 'docs');
	const samples = path.join(repository, 'samples');
	const sampleBuild = path.join(root, 'sample-build');
	const apiXml = path.join(root, 'SIL.Motif.Contract.xml');

	await mkdir(path.join(help, 'commands'), { recursive: true });
	await mkdir(path.join(help, 'terms'), { recursive: true });
	await mkdir(path.join(help, 'ui'), { recursive: true });
	await mkdir(path.join(help, 'guide', 'agents'), { recursive: true });
	await mkdir(path.join(help, 'guide', 'learn'), { recursive: true });
	await mkdir(path.join(docs, 'adr'), { recursive: true });
	await mkdir(path.join(samples, 'synthetic-turkic'), { recursive: true });
	await mkdir(sampleBuild, { recursive: true });
	await mkdir(path.join(walks, 'open-project'), { recursive: true });
	await mkdir(path.join(walks, 'open-project', 'steps'), { recursive: true });
	const exportedHelp = {
		locale: 'en',
		siteRoot: 'https://motif-docs.pages.dev',
		entries: [
			{
				kind: 'command',
				code: 'open project',
				slug: 'open-project',
				title: 'Open a project',
				description: 'Loads a language project into Motif.',
				helpPage: '# Open a project\n\nLearn about [Proposal](term:proposal), [the agent guide](guide:agents/start-here), [lessons](guide:learn/index), and [stems](guide:learn/stems-and-the-lexicon).\n\n![Project view](shot:open-project/overview)\n',
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
				helpPage: '# Proposal\n\nA named set of changes.\n',
				url: 'https://motif-docs.pages.dev/reference/terms/proposal/',
			},
			{
				kind: 'ui',
				code: 'Motif.Overview.Refresh',
				slug: 'motif-overview-refresh',
				title: 'Refresh numbers',
				description: 'Capture a new Baseline and measure it again.',
				helpPage: '# Refresh numbers\n\nMeasure after FieldWorks saves.\n',
				url: 'https://motif-docs.pages.dev/reference/controls/motif-overview-refresh/',
			},
			...fixtureGuideEntries({
				'what-is-motif': {
					title: 'What Motif does',
					description: 'Measure a grammar and try changes safely.',
					helpPage: '# What Motif does\n\nMeasure a grammar and try changes safely.\n\n![Picture coming later](shot:not-built/step-one)\n',
				},
				'open-a-project': {
					title: 'Opening a project',
					description: 'Open a FieldWorks project.',
					helpPage: '# Opening a project\n\nOpen a project with [open project](cmd:open%20project).\n',
				},
				'agents/start-here': {
					title: 'Using Motif from an agent',
					description: 'Start with the agent workflow.',
					helpPage: '# Using Motif from an agent\n\nStart with [Proposal](term:proposal).\n',
				},
				'overview': {
					title: 'Catalog Overview',
					description: 'Catalog description for the Overview page.',
				},
				'learn/index': {
					title: 'Teach a dumb computer your language',
					description: 'An authored introduction to Learn.',
					helpPage: '# Teach a dumb computer your language\n\nThe authored Learn introduction survives sync. Read [Proposal](term:proposal).\n',
				},
				'learn/synthetic-turkic-plural-harmony': {
					title: 'Fix vowel harmony',
					description: 'Practice fixing a synthetic grammar.',
					helpPage: '# Fix vowel harmony\n\nRead the failing word with [Try a Word](cmd:open%20project).\n',
				},
			}),
		],
	};
	await writeFile(
		path.join(repository, 'help-export.json'),
		JSON.stringify(exportedHelp),
	);
	await writeFile(
		path.join(help, 'commands', 'open-project.md'),
		'# Physical command copy\n\nThis must not replace exported command content.\n',
	);
	await writeFile(path.join(help, 'terms', 'proposal.md'), '# Physical term copy\n\nThis must not replace exported term content.\n');
	await writeFile(path.join(help, 'ui', 'motif-overview-refresh.md'), '# Physical control copy\n\nThis must not replace exported control content.\n');
	await writeFile(path.join(help, 'guide', 'what-is-motif.md'), '# Physical Guide copy\n\nThis must not replace exported Guide content.\n');
	await writeFile(path.join(help, 'guide', 'open-a-project.md'), '# Physical Guide copy\n\nThis must not replace exported Guide content.\n');
	await writeFile(path.join(help, 'guide', 'agents', 'start-here.md'), '# Physical Guide copy\n\nThis must not replace exported Guide content.\n');
	await writeFile(path.join(help, 'guide', 'learn', 'index.md'), '# Physical Learn index copy\n\nThe export owns the Learn introduction.\n');
	await writeFile(path.join(help, 'guide', 'learn', 'synthetic-turkic-plural-harmony.md'), '# Physical Learn copy\n\nThis must not replace exported Learn content.\n');
	const orderedLearnLessons = [
		'what-the-parser-knows',
		'stems-and-the-lexicon',
		'affixes-slots-and-templates',
		'allomorphs-and-environments',
		'phonological-rules',
		'modelling-a-grammar-the-parser-can-use',
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
	const term = await readFile(path.join(site, 'src', 'content', 'docs', 'reference', 'terms', 'proposal.md'), 'utf8');
	const control = await readFile(path.join(site, 'src', 'content', 'docs', 'reference', 'controls', 'motif-overview-refresh.md'), 'utf8');
	const samplesPage = await readFile(path.join(site, 'src', 'content', 'docs', 'samples', 'index.md'), 'utf8');

	assert.match(command, /\/reference\/terms\/proposal\//);
	assert.match(command, /Learn about/);
	assert.doesNotMatch(command, /Physical command copy/);
	assert.match(command, /\/walkthroughs\/open-project\/steps\/01-overview\.png/);
	assert.ok(command.includes('[the agent guide](/guide/agents/start-here/)'));
	assert.ok(command.includes('[lessons](/learn/)'));
	assert.ok(command.includes('[stems](/learn/stems-and-the-lexicon/)'));
	assert.match(command, /Surface: Released/);
	assert.match(command, /Usage: motif open project/);
	assert.doesNotMatch(command, /^# Open a project$/m);
	assert.match(guideIndex, /What Motif does[\s\S]*Opening a project[\s\S]*Using Motif from an agent/);
	assert.match(guideIndex, /Catalog Overview[\s\S]*\/guide\/overview\//);
	assert.match(guideStart, /\/reference\/terms\/proposal\//);
	assert.doesNotMatch(guideStart, /^# Using Motif from an agent$/m);
	assert.match(guideWhat, /Picture coming later/);
	assert.doesNotMatch(guideWhat, /Physical Guide copy/);
	assert.doesNotMatch(guideWhat, /shot:not-built/);
	assert.match(term, /A named set of changes\./);
	assert.doesNotMatch(term, /Physical term copy/);
	assert.match(control, /Measure after FieldWorks saves/);
	assert.doesNotMatch(control, /Physical control copy/);
	await assert.rejects(access(path.join(site, 'src', 'content', 'docs', 'developers', 'adr')));
	assert.match(design, /https:\/\/github\.com\/sillsdev\/motif\/blob\/main\/docs\/adr\/0001-example\.md/);
	assert.match(issueDoc, /Issue records belong to the Developer section\./);
	assert.match(api, /## `SIL\.Motif\.Contract\.MotifJson`/);
	assert.match(api, /### `CreateOptions`/);
	assert.match(api, /Creates fresh options\./);
	assert.match(walkthroughPage, /<Walkthrough/);
	assert.equal(await readFile(path.join(site, 'public', 'walkthroughs', 'open-project', 'clip.webm'), 'utf8'), 'video');
	assert.match(learnPage, /Fix vowel harmony/);
	assert.doesNotMatch(learnPage, /Physical Learn copy/);
	assert.match(learnPage, /\/reference\/commands\/open-project\//);
	assert.match(learnIndex, /The authored Learn introduction survives sync\./);
	assert.doesNotMatch(learnIndex, /Physical Learn index copy/);
	assert.match(learnIndex, /\/reference\/terms\/proposal\//);
	const featureCards = JSON.parse(await readFile(path.join(site, 'src', 'data', 'guide-features.json'), 'utf8'));
	assert.deepEqual(featureCards[0], {
		title: 'Catalog Overview',
		description: 'Catalog description for the Overview page.',
		href: '/guide/overview/',
	});
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
	exportedHelp.entries.push(guideEntry('learn/synthetic-turkic-speed-benchmark', {
		title: 'Compare parsing before and after a fix',
		description: 'This lesson measures parse results before and after repairing the sample.',
		helpPage: '# Compare parsing before and after a fix\n\nThis lesson measures parse results before and after repairing the sample.\n',
	}));
	await writeFile(path.join(repository, 'help-export.json'), JSON.stringify(exportedHelp));
	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });
	const learnIndexWithSpeed = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	assert.match(learnIndexWithSpeed, /The authored Learn introduction survives sync\./);
	assert.doesNotMatch(learnIndexWithSpeed, /synthetic-turkic-speed-benchmark/);
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
	exportedHelp.entries = exportedHelp.entries.filter((entry) => entry.kind !== 'guide'
		|| !entry.code.startsWith('learn/synthetic-turkic-'));
	exportedHelp.entries.find((entry) => entry.kind === 'command').helpPage =
		'# Open a project\n\nRead [Proposal](term:proposal) and [the agent guide](guide:agents/start-here).\n';
	await writeFile(path.join(repository, 'help-export.json'), JSON.stringify(exportedHelp));
	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });
	await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	await assert.rejects(readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'synthetic-turkic-plural-harmony.md'), 'utf8'), { code: 'ENOENT' });
	await assert.rejects(readFile(path.join(site, 'src', 'data', 'synthetic-turkic-performance.json'), 'utf8'), { code: 'ENOENT' });
	assert.equal(JSON.parse(await readFile(path.join(site, 'src', 'data', 'samples.json'), 'utf8'))[0].lessonsHref, null);
	exportedHelp.entries.push(guideEntry('unlisted-page'));
	await writeFile(path.join(repository, 'help-export.json'), JSON.stringify(exportedHelp));
	await assert.rejects(
		syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild }),
		/Guide page is missing from the published outline: unlisted-page/,
	);
	exportedHelp.entries = exportedHelp.entries.filter((entry) => entry.kind !== 'guide' || entry.code !== 'install');
	await writeFile(path.join(repository, 'help-export.json'), JSON.stringify(exportedHelp));
	await assert.rejects(
		syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild }),
		/Guide outline references missing Guide entry: install/,
	);
	exportedHelp.entries.push(guideEntry('overview'));
	await writeFile(path.join(repository, 'help-export.json'), JSON.stringify(exportedHelp));
	await assert.rejects(
		syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild }),
		/Duplicate help entry: guide overview/,
	);
});

test('sync renders Guide metadata and content from the exported catalog', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-site-guide-catalog-'));
	t.after(() => rm(root, { recursive: true, force: true }));

	const repository = path.join(root, 'repo');
	const site = path.join(repository, 'site');
	const helpRoot = path.join(repository, 'help');
	const help = path.join(helpRoot, 'en');
	const walks = path.join(root, 'walks');
	const docs = path.join(repository, 'docs');
	const samples = path.join(repository, 'samples');
	const sampleBuild = path.join(root, 'sample-build');
	const apiXml = path.join(root, 'SIL.Motif.Contract.xml');
	const helpExportPath = path.join(repository, 'help-export.json');

	await mkdir(path.join(helpRoot, 'en', 'guide'), { recursive: true });
	await mkdir(docs, { recursive: true });
	await mkdir(samples, { recursive: true });
	await mkdir(sampleBuild, { recursive: true });
	await writeFile(path.join(helpRoot, 'en', 'guide', 'what-is-motif.md'), '# Physical title\n\nPhysical content.\n');
	await writeFile(apiXml, '<doc><members></members></doc>');
	await writeFile(helpExportPath, JSON.stringify({
		locale: 'en',
		siteRoot: 'https://motif-docs.pages.dev',
		entries: fixtureGuideEntries({
			'what-is-motif': {
				title: 'Catalog title',
				description: 'Catalog description.',
				helpPage: '# Catalog title\n\nCatalog content.\n',
			},
		}),
	}));

	await syncSiteContent({
		repository,
		site,
		helpExportPath,
		helpRoot,
		walkthroughRoot: walks,
		docsRoot: docs,
		apiXmlPath: apiXml,
		samplesRoot: samples,
		samplesOut: sampleBuild,
	});

	const page = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'what-is-motif.md'), 'utf8');
	assert.match(page, /title: "Catalog title"/);
	assert.match(page, /description: "Catalog description\."/);
	assert.match(page, /Catalog content\./);
	assert.doesNotMatch(page, /Physical title|Physical content/);
	const guideIndex = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'index.md'), 'utf8');
	assert.match(guideIndex, /\[Catalog title\]\(\/guide\/what-is-motif\/\)/);
	const features = JSON.parse(await readFile(path.join(site, 'src', 'data', 'guide-features.json'), 'utf8'));
	assert.equal(features[0].title, 'overview');
	const sentinel = path.join(site, 'src', 'route-escape.md');
	await writeFile(sentinel, 'Keep this file unchanged.');
	const exported = JSON.parse(await readFile(helpExportPath, 'utf8'));
	const target = exported.entries.find((entry) => entry.kind === 'guide' && entry.code === 'what-is-motif');
	for (const separator of ['%2f', '%5c']) {
		target.url = 'https://motif-docs.pages.dev/guide/ok' + separator + '..' + separator + '..' + separator + '..' + separator + '..' + separator + 'route-escape/';
		await writeFile(helpExportPath, JSON.stringify(exported));
		await assert.rejects(syncSiteContent({
			repository, site, helpExportPath, helpRoot, walkthroughRoot: walks,
			docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild,
		}), /Invalid help entry route: guide what-is-motif/);
		assert.equal(await readFile(sentinel, 'utf8'), 'Keep this file unchanged.');
	}
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
		env: {
			...process.env,
			MOTIF_WORKER_ROOT: path.join(root, 'worker'),
			MOTIF_RUNNER_NAMESPACE: path.basename(root),
		},
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
	const fixtureRoot = path.join(repositoryRoot, 'site', 'fixtures');
	await syncSiteContent({
		repository: repositoryRoot,
		site,
		helpExportPath,
		helpRoot: path.join(repositoryRoot, 'src', 'SIL.Motif.Help', 'Content'),
		walkthroughRoot: path.join(fixtureRoot, 'walkthroughs'),
		docsRoot: path.join(repositoryRoot, 'docs'),
		apiXmlPath: path.join(fixtureRoot, 'SIL.Motif.Contract.xml'),
		samplesRoot: path.join(fixtureRoot, 'samples'),
		samplesOut: path.join(fixtureRoot, 'samples'),
	});

	const entries = new Map(helpExport.entries.map((entry) => [`${entry.kind}:${entry.code}`, entry]));
	const expectedPages = [
		['command', 'assess'],
		['term', 'proposal'],
		['guide', 'overview'],
		['guide', 'agents/handoff'],
		['guide', 'agents/start-here'],
		['guide', 'learn/index'],
		['guide', 'learn/stems-and-the-lexicon'],
	];
	const exportedControl = helpExport.entries.find((entry) => entry.kind === 'ui');
	if (exportedControl) expectedPages.splice(2, 0, ['ui', exportedControl.code]);
	for (const entry of helpExport.entries) {
		assert.ok(entry.helpPage === null || typeof entry.helpPage === 'string', `${entry.kind} ${entry.code} must export a Help page or null.`);
		if (entry.kind === 'guide') assert.equal(typeof entry.helpPage, 'string', `Guide ${entry.code} must export its Help page.`);
	}
	for (const [kind, code] of expectedPages) {
		const entry = entries.get(`${kind}:${code}`);
		assert.ok(entry, `The CLI export must include ${kind} ${code}.`);
		assert.ok(entry.helpPage === null || typeof entry.helpPage === 'string', `${kind} ${code} must export a Help page or null.`);
		const urlPath = new URL(entry.url).pathname.replace(/\/$/, '');
		const relativePath = kind === 'guide'
			? urlPath === '/learn' ? 'learn/index.md' : urlPath.startsWith('/learn/')
				? `learn/${urlPath.slice('/learn/'.length)}.md` : `guide/${urlPath.slice('/guide/'.length)}.md`
			: kind === 'command' ? `reference/commands/${urlPath.slice('/reference/commands/'.length)}.md`
				: kind === 'term' ? `reference/terms/${urlPath.slice('/reference/terms/'.length)}.md`
					: `reference/controls/${urlPath.slice('/reference/controls/'.length)}.md`;
		const page = await readFile(path.join(site, 'src', 'content', 'docs', relativePath), 'utf8');
		assert.ok(page.includes(`title: ${JSON.stringify(entry.title)}`), `${kind} ${code} must use its exported title.`);
		assert.ok(page.includes(`description: ${JSON.stringify(entry.description)}`), `${kind} ${code} must use its exported description.`);
		if (typeof entry.helpPage === 'string') {
			const content = entry.helpPage.split(/\r?\n\s*\r?\n/).slice(1).join(' ');
			const contentMarker = content.replace(/\[[^\]]+\]\([^)]+\)/g, '').trim().split(/\s+/).slice(0, 5).join(' ');
			if (contentMarker) assert.ok(page.includes(contentMarker), `${kind} ${code} must render its exported Help page.`);
		} else {
			assert.ok(page.includes(entry.description), `${kind} ${code} must render its exported description when no Help page exists.`);
		}
	}

	const guideEntries = helpExport.entries.filter((entry) => entry.kind === 'guide');
	for (const entry of guideEntries) {
		const urlPath = new URL(entry.url).pathname.replace(/\/$/, '');
		const relativePath = urlPath === '/learn' ? 'learn/index.md' : urlPath.startsWith('/learn/')
			? `learn/${urlPath.slice('/learn/'.length)}.md` : `guide/${urlPath.slice('/guide/'.length)}.md`;
		await access(path.join(site, 'src', 'content', 'docs', relativePath));
	}
	const guideIndex = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'index.md'), 'utf8');
	const pangloss = entries.get('guide:pangloss');
	assert.ok(pangloss);
	assert.ok(guideIndex.includes(`[${pangloss.title}](/guide/pangloss/)`));
	const featureCards = JSON.parse(await readFile(path.join(site, 'src', 'data', 'guide-features.json'), 'utf8'));
	assert.deepEqual(featureCards.map(({ title, description }) => ({ title, description })),
		['overview', 'texts', 'try-a-word', 'timing', 'warnings', 'review-changes', 'ai-handoff'].map((code) => {
			const entry = entries.get(`guide:${code}`);
			assert.ok(entry, `The CLI export must include Guide ${code}.`);
			return { title: entry.title, description: entry.description };
		}));
	const turkicSample = JSON.parse(await readFile(path.join(site, 'src', 'data', 'samples.json'), 'utf8'))
		.find((sample) => sample.id === 'synthetic-turkic');
	assert.ok(turkicSample, 'the real export sync must include the Turkic teaching sample');
	const turkicGuides = guideEntries.filter((entry) => {
		if (!entry.code.startsWith('learn/')) return false;
		const lesson = entry.code.slice('learn/'.length).split('/')[0];
		return lesson.startsWith('turkish-') || lesson.startsWith('synthetic-turkic-');
	});
	if (turkicGuides.length === 0) {
		assert.equal(turkicSample.lessonsHref, null);
	} else {
		assert.ok(turkicGuides.some((entry) => new URL(entry.url).pathname === turkicSample.lessonsHref),
			'A sample lesson link must match a Learn route exported by the CLI.');
	}
});
