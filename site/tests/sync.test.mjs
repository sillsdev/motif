import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { syncSiteContent } from '../scripts/sync-core.mjs';

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
	await mkdir(path.join(samples, 'sample-turkish'), { recursive: true });
	await mkdir(sampleBuild, { recursive: true });
	await mkdir(path.join(walks, 'open-project'), { recursive: true });
	await mkdir(path.join(walks, 'open-project', 'steps'), { recursive: true });
	await writeFile(
		path.join(repository, 'help-export.json'),
		JSON.stringify({
			locale: 'en',
			siteRoot: 'https://motif-docs.pages.dev',
			entries: [
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
		'# Open a project\n\nLearn about [Proposal](term:proposal).\n\n![Project view](shot:open-project/overview)\n',
	);
	await writeFile(path.join(help, 'terms', 'proposal.md'), '# Proposal\n\nA named set of changes.\n');
	await writeFile(path.join(help, 'guide', 'what-is-motif.md'), '# What Motif does\n\nMeasure a grammar and try changes safely.\n\n![Picture coming later](shot:not-built/step-one)\n');
	await writeFile(path.join(help, 'guide', 'open-a-project.md'), '# Opening a project\n\nOpen a project with [open project](cmd:open%20project).\n');
	await writeFile(path.join(help, 'guide', 'agents', 'start-here.md'), '# Using Motif from an agent\n\nStart with [Proposal](term:proposal).\n');
	await writeFile(path.join(help, 'guide', 'learn', 'turkish-plural-harmony.md'), '# Fix vowel harmony\n\nRead the failing word with [Try a Word](cmd:open%20project).\n');
	await writeFile(path.join(samples, 'sample-turkish', 'sample.json'), JSON.stringify({
		id: 'sample-turkish',
		title: 'Turkish (teaching sample)',
		language: { name: 'Turkish', tag: 'tr' },
		teaches: ['suffix slots in order', 'vowel harmony'],
		summary: 'Practice measuring and repairing a small teaching grammar.',
		disclaimer: 'A simplified teaching grammar for learning Motif; not a description of Turkish.',
	}));
	await writeFile(path.join(sampleBuild, 'sample-turkish-fixed.fwbackup'), 'fixed-project');
	await writeFile(path.join(docs, 'adr', '0001-example.md'), '---\nlayout: old\n---\n\n# Example decision\n\n<!-- remove this -->\n\n[Design](../design.md#rules)\n');
	await writeFile(path.join(docs, 'design.md'), '# Design\n\n## Rules\n');
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
	const developer = await readFile(path.join(site, 'src', 'content', 'docs', 'developers', 'adr', '0001-example.md'), 'utf8');
	const developerIndex = await readFile(path.join(site, 'src', 'content', 'docs', 'developers', 'adr', 'index.md'), 'utf8');
	const issueDoc = await readFile(path.join(site, 'src', 'content', 'docs', 'developers', 'issues.md'), 'utf8');
	const api = await readFile(path.join(site, 'src', 'content', 'docs', 'reference', 'api', 'index.md'), 'utf8');
	const walkthroughPage = await readFile(path.join(site, 'src', 'content', 'docs', 'guide', 'walkthroughs', 'open-project.mdx'), 'utf8');
	const learnPage = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'turkish-plural-harmony.md'), 'utf8');
	const learnIndex = await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	const samplesPage = await readFile(path.join(site, 'src', 'content', 'docs', 'samples', 'index.md'), 'utf8');

	assert.match(command, /\/reference\/terms\/proposal\//);
	assert.match(command, /\/walkthroughs\/open-project\/steps\/01-overview\.png/);
	assert.match(command, /Surface: Released/);
	assert.match(command, /Usage: motif open project/);
	assert.doesNotMatch(command, /^# Open a project$/m);
	assert.match(guideIndex, /What Motif does[\s\S]*Opening a project[\s\S]*Using Motif from an agent/);
	assert.match(guideStart, /\/reference\/terms\/proposal\//);
	assert.doesNotMatch(guideStart, /^# Using Motif from an agent$/m);
	assert.match(guideWhat, /Picture coming later/);
	assert.doesNotMatch(guideWhat, /shot:not-built/);
	assert.doesNotMatch(developer, /layout: old|remove this/);
	assert.match(developer, /\/developers\/design\/#rules/);
	assert.match(developerIndex, /Example decision/);
	assert.match(issueDoc, /Issue records belong to the Developer section\./);
	assert.match(api, /## `SIL\.Motif\.Contract\.MotifJson`/);
	assert.match(api, /### `CreateOptions`/);
	assert.match(api, /Creates fresh options\./);
	assert.match(walkthroughPage, /<Walkthrough/);
	assert.equal(await readFile(path.join(site, 'public', 'walkthroughs', 'open-project', 'clip.webm'), 'utf8'), 'video');
	assert.match(learnPage, /Fix vowel harmony/);
	assert.match(learnPage, /\/reference\/commands\/open-project\//);
	assert.match(learnIndex, /Fix vowel harmony/);
	assert.match(samplesPage, /Turkish \(teaching sample\)/);
	assert.match(samplesPage, /suffix slots in order, vowel harmony/);
	assert.match(samplesPage, /A simplified teaching grammar for learning Motif/);
	assert.match(samplesPage, /\/downloads\/samples\/sample-turkish-fixed\.fwbackup/);
	assert.match(samplesPage, /Broken project \(\.fwbackup\) is available in release builds/);
	assert.match(samplesPage, /id="sample-turkish"/);
	assert.doesNotMatch(samplesPage, /Includes a speed lesson/);
	assert.equal(await readFile(path.join(site, 'public', 'downloads', 'samples', 'sample-turkish-fixed.fwbackup'), 'utf8'), 'fixed-project');
	await assert.rejects(readFile(path.join(site, 'public', 'downloads', 'samples', 'sample-turkish-broken.fwbackup'), 'utf8'), { code: 'ENOENT' });
	await assert.rejects(readFile(path.join(site, 'src', 'data', 'sample-turkish-performance.json'), 'utf8'), { code: 'ENOENT' });

	await writeFile(path.join(samples, 'sample-turkish', 'expected.json'), JSON.stringify({
		fixed: { words: 2, parsed: 2, textCoverage: 1 },
		broken: { words: 2, parsed: 1, textCoverage: 0.5, failing: { 'evler': 'plural-harmony' } },
	}));
	await writeFile(path.join(sampleBuild, 'sample-turkish-broken.fwbackup'), 'broken-project');
	await writeFile(path.join(help, 'guide', 'learn', 'turkish-speed-benchmark.md'), '# Compare parsing before and after a fix\n\nThis lesson measures parse results before and after repairing the sample.\n');
	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });
	assert.match(await readFile(path.join(site, 'src', 'content', 'docs', 'samples', 'index.md'), 'utf8'), /Includes a speed lesson/);
	assert.equal(await readFile(path.join(site, 'public', 'downloads', 'samples', 'sample-turkish-broken.fwbackup'), 'utf8'), 'broken-project');
	assert.deepEqual(JSON.parse(await readFile(path.join(site, 'src', 'data', 'samples.json'), 'utf8'))[0].speedLesson, true);
	assert.deepEqual(JSON.parse(await readFile(path.join(site, 'src', 'data', 'sample-turkish-performance.json'), 'utf8')), {
		fixed: { words: 2, parsed: 2, textCoverage: 1 },
		broken: { words: 2, parsed: 1, textCoverage: 0.5 },
	});

	await rm(path.join(help, 'guide', 'learn'), { recursive: true, force: true });
	await rm(path.join(samples, 'sample-turkish', 'expected.json'));
	await syncSiteContent({ repository, site, helpExportPath: path.join(repository, 'help-export.json'), helpRoot: path.join(repository, 'help'), walkthroughRoot: walks, docsRoot: docs, apiXmlPath: apiXml, samplesRoot: samples, samplesOut: sampleBuild });
	await readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'index.md'), 'utf8');
	await assert.rejects(readFile(path.join(site, 'src', 'content', 'docs', 'learn', 'turkish-plural-harmony.md'), 'utf8'), { code: 'ENOENT' });
	await assert.rejects(readFile(path.join(site, 'src', 'data', 'sample-turkish-performance.json'), 'utf8'), { code: 'ENOENT' });
});
