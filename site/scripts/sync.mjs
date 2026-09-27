import { access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { syncSiteContent } from './sync-core.mjs';

const site = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const repository = path.resolve(site, '..');

function argument(name) {
	const index = process.argv.indexOf(name);
	if (index >= 0) return process.argv[index + 1];
	const inline = process.argv.find((value) => value.startsWith(`${name}=`));
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

const fixtureRoot = path.join(site, 'fixtures');
const helpExportOption = argument('--help-export') ?? process.env.MOTIF_HELP_EXPORT;
const helpExportPath = helpExportOption ?? path.join(fixtureRoot, 'help-export.json');
const helpRoot = argument('--help-root') ?? process.env.MOTIF_HELP_CONTENT_ROOT ?? (helpExportOption
	? await firstExisting(path.join(repository, 'help'), path.join(fixtureRoot, 'help'))
	: path.join(fixtureRoot, 'help'));
const walkthroughRoot = argument('--walkthrough-output') ?? process.env.MOTIF_WALKTHROUGH_OUTPUT ?? path.join(fixtureRoot, 'walkthroughs');
const samplesRootOption = argument('--samples-root');
const samplesOutOption = argument('--samples-out');
const samplesRoot = samplesRootOption
	? path.resolve(samplesRootOption)
	: await firstExisting(path.join(repository, 'samples'), path.join(fixtureRoot, 'samples'));
const samplesOut = samplesOutOption
	? path.resolve(samplesOutOption)
	: await firstExisting(path.join(repository, 'bin', 'Debug', 'samples'), path.join(fixtureRoot, 'samples'));
const docsRoot = path.join(repository, 'docs');
const apiXmlPath = argument('--api-xml') ?? process.env.MOTIF_CONTRACT_XML ?? await firstExisting(
	path.join(repository, 'bin', 'Debug', 'SIL.Motif.Contract.xml'),
	path.join(fixtureRoot, 'SIL.Motif.Contract.xml'),
);

const result = await syncSiteContent({ repository, site, helpExportPath, helpRoot, walkthroughRoot, docsRoot, apiXmlPath, samplesRoot, samplesOut });
process.stdout.write(`Synced ${result.entries} help entries, ${result.walkthroughs} Walkthroughs, ${result.samples} samples, and ${result.learnLessons} Learn lessons.\n`);
