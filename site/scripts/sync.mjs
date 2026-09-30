import { access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { syncSiteContent } from './sync-core.mjs';
import { validateProductionSyncArguments, validateProductionHelpExport } from './production-inputs.mjs';
import { validateWalkthroughArtifacts } from './walkthrough-artifacts.mjs';

const site = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repository = path.resolve(site, '..');

function argument(argv, name) {
	const index = argv.indexOf(name);
	if (index >= 0) return argv[index + 1];
	const inline = argv.find((value) => value.startsWith(name + '='));
	return inline?.slice(name.length + 1);
}

async function firstExisting(...paths) {
	for (const candidate of paths) {
		try {
			await access(candidate);
			return candidate;
		} catch {
		}
	}
	return paths[0];
}

function productionArguments(argv) {
	if (process.env.MOTIF_SITE_SYNC_MODE !== 'production') return argv;
	const result = [...argv];
	if (!result.includes('--production')) result.push('--production');
	const environmentOptions = [
		['--help-export', 'MOTIF_HELP_EXPORT'],
		['--help-root', 'MOTIF_HELP_CONTENT_ROOT'],
		['--walkthrough-output', 'MOTIF_WALKTHROUGH_OUTPUT'],
		['--api-xml', 'MOTIF_CONTRACT_XML'],
		['--samples-root', 'MOTIF_SAMPLES_ROOT'],
		['--samples-out', 'MOTIF_SAMPLES_OUT'],
		['--docs-root', 'MOTIF_DOCS_ROOT'],
		['--configuration', 'MOTIF_BUILD_CONFIGURATION'],
	];
	for (const [option, variable] of environmentOptions) {
		if (process.env[variable]) result.push(option, process.env[variable]);
	}
	if (process.env.MOTIF_WALKTHROUGH_REQUIRE_CLIPS === '1') result.push('--require-videos');
	return result;
}

const argv = productionArguments(process.argv.slice(2));
const production = validateProductionSyncArguments(argv, { repository, site });
const fixtureRoot = path.join(site, 'fixtures');
let helpExportPath;
let helpRoot;
let walkthroughRoot;
let samplesRoot;
let samplesOut;
let docsRoot;
let apiXmlPath;

if (production) {
	helpExportPath = production.helpExport;
	helpRoot = production.helpRoot;
	walkthroughRoot = production.walkthroughOutput;
	samplesRoot = production.samplesRoot;
	samplesOut = production.samplesOut;
	docsRoot = production.docsRoot;
	apiXmlPath = production.apiXml;
	await validateProductionHelpExport(helpExportPath, helpRoot);
	await validateWalkthroughArtifacts({
		repository,
		output: walkthroughRoot,
		requireVideos: production.requireVideos,
	});
} else {
	const helpExportOption = argument(argv, '--help-export') ?? process.env.MOTIF_HELP_EXPORT;
	helpExportPath = helpExportOption ?? path.join(fixtureRoot, 'help-export.json');
	helpRoot = argument(argv, '--help-root') ?? process.env.MOTIF_HELP_CONTENT_ROOT ?? (helpExportOption
		? await firstExisting(path.join(repository, 'help'), path.join(fixtureRoot, 'help'))
		: path.join(fixtureRoot, 'help'));
	walkthroughRoot = argument(argv, '--walkthrough-output') ??
		process.env.MOTIF_WALKTHROUGH_OUTPUT ?? path.join(fixtureRoot, 'walkthroughs');
	const samplesRootOption = argument(argv, '--samples-root');
	const samplesOutOption = argument(argv, '--samples-out');
	samplesRoot = samplesRootOption
		? path.resolve(samplesRootOption)
		: await firstExisting(path.join(repository, 'samples'), path.join(fixtureRoot, 'samples'));
	samplesOut = samplesOutOption
		? path.resolve(samplesOutOption)
		: await firstExisting(path.join(repository, 'bin', 'Debug', 'samples'), path.join(fixtureRoot, 'samples'));
	docsRoot = path.join(repository, 'docs');
	apiXmlPath = argument(argv, '--api-xml') ?? process.env.MOTIF_CONTRACT_XML ?? await firstExisting(
		path.join(repository, 'bin', 'Debug', 'SIL.Motif.Contract.xml'),
		path.join(fixtureRoot, 'SIL.Motif.Contract.xml'),
	);
}

const result = await syncSiteContent({
	repository, site, helpExportPath, helpRoot, walkthroughRoot, docsRoot, apiXmlPath, samplesRoot, samplesOut,
});
process.stdout.write('Synced ' + result.entries + ' help entries, ' + result.walkthroughs +
	' Walkthroughs, ' + result.samples + ' samples, and ' + result.learnLessons + ' Learn lessons.\n');
