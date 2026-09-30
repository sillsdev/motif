import { access, readFile, readdir, stat } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import path from 'node:path';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
const requiredEncoders = ['libx264', 'libvpx-vp9', 'libwebp_anim'];

export async function validateWalkthroughArtifacts({ repository, output, requireVideos = false, ffmpegExecutable = 'ffmpeg' }) {
	const scriptsRoot = path.join(repository, 'walkthroughs');
	const scriptFiles = (await readdir(scriptsRoot, { withFileTypes: true }))
		.filter((entry) => entry.isFile() && entry.name.endsWith('.walkthrough.json'))
		.map((entry) => path.join(scriptsRoot, entry.name))
		.sort();
	if (!scriptFiles.length) throw new Error(`No Walkthrough scripts were found in ${scriptsRoot}.`);

	const expected = [];
	for (const scriptPath of scriptFiles) {
		const script = JSON.parse(await readFile(scriptPath, 'utf8'));
		if (!script.id || !Array.isArray(script.steps)) {
			throw new Error(`Invalid Walkthrough script: ${scriptPath}`);
		}
		const captureIds = script.steps.filter((step) => step.kind === 'capture').map((step) => step.id);
		if (!captureIds.length) throw new Error(`Walkthrough '${script.id}' has no screenshot steps.`);
		expected.push({ id: script.id, captureIds });
	}

	const outputEntries = await readdir(output, { withFileTypes: true });
	const outputIds = outputEntries.filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort();
	for (const item of expected) {
		if (!outputIds.includes(item.id)) {
			throw new Error(`Walkthrough artifacts are missing for discovered script '${item.id}'.`);
		}
		await validateWalkthroughDirectory(path.join(output, item.id), item, requireVideos);
	}
	const expectedIds = new Set(expected.map((item) => item.id));
	const extra = outputIds.find((id) => !expectedIds.has(id));
	if (extra) throw new Error(`Walkthrough output contains an undiscovered script '${extra}'.`);

	if (requireVideos) await validateFfmpeg(ffmpegExecutable);
	return { walkthroughs: expected.length, screenshots: expected.reduce((total, item) => total + item.captureIds.length, 0) };
}

export function validateEncoderCapabilities(encoderOutput) {
	const missing = requiredEncoders.filter((name) => !new RegExp(`\\b${name}\\b`).test(encoderOutput));
	if (missing.length) throw new Error(`ffmpeg is missing required encoders: ${missing.join(', ')}.`);
}

async function validateWalkthroughDirectory(directory, expected, requireVideos) {
	const manifestPath = path.join(directory, 'manifest.json');
	let manifest;
	try {
		manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
	} catch (error) {
		if (error.code === 'ENOENT') throw new Error(`Walkthrough '${expected.id}' is missing manifest.json.`);
		throw error;
	}
	if (manifest.id !== expected.id || !Array.isArray(manifest.steps)) {
		throw new Error(`Walkthrough '${expected.id}' has an invalid manifest.`);
	}
	const actualIds = manifest.steps.map((step) => step.id);
	if (actualIds.length !== expected.captureIds.length ||
		actualIds.some((id, index) => id !== expected.captureIds[index])) {
		throw new Error(`Walkthrough '${expected.id}' manifest does not cover every scripted screenshot step.`);
	}

	for (const [index, step] of manifest.steps.entries()) {
		for (const asset of [step.screenshot, step.annotated]) {
			await requireFile(directory, asset, `Walkthrough '${expected.id}' is missing screenshot '${asset}'.`);
		}
		if (!step.caption) throw new Error(`Walkthrough '${expected.id}' screenshot '${step.id}' has no caption.`);
	}
	await requireFile(directory, `captions.${manifest.locale}.vtt`,
		`Walkthrough '${expected.id}' is missing captions.${manifest.locale}.vtt.`);

	if (requireVideos) {
		if (!manifest.clip) throw new Error(`Release sync requires video assets for walkthrough '${expected.id}'.`);
		for (const asset of [manifest.clip.mp4, manifest.clip.webm, manifest.clip.poster]) {
			if (!asset) throw new Error(`Release sync requires MP4, WebM and poster assets for walkthrough '${expected.id}'.`);
			await requireFile(directory, asset,
				`Release sync requires MP4, WebM and poster assets for walkthrough '${expected.id}'; missing '${asset}'.`);
		}
		if (manifest.clip.webp) await requireFile(directory, manifest.clip.webp,
			`Walkthrough '${expected.id}' manifest declares a missing WebP asset '${manifest.clip.webp}'.`);
	}
}

async function requireFile(root, relative, message) {
	if (typeof relative !== 'string' || !relative.trim()) throw new Error(message);
	const candidate = path.resolve(root, relative);
	const within = path.relative(root, candidate);
	if (!within || within === '..' || within.startsWith(`..${path.sep}`) || path.isAbsolute(within)) {
		throw new Error(`Walkthrough asset escapes its directory: ${relative}`);
	}
	try {
		await access(candidate);
		const details = await stat(candidate);
		if (!details.isFile() || details.size === 0) throw new Error(message);
	} catch (error) {
		if (error.code === 'ENOENT') throw new Error(message);
		throw error;
	}
}

async function validateFfmpeg(ffmpegExecutable) {
	let result;
	try {
		result = await execFileAsync(ffmpegExecutable, ['-hide_banner', '-encoders'], {
			encoding: 'utf8',
			maxBuffer: 4 * 1024 * 1024,
			windowsHide: true,
		});
	} catch (error) {
		throw new Error(`Required ffmpeg encoders are unavailable: ${error.message}`);
	}
	validateEncoderCapabilities(`${result.stdout}\n${result.stderr}`);
}
