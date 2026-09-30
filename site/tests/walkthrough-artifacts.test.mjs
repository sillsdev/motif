import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import * as walkthroughArtifacts from '../scripts/walkthrough-artifacts.mjs';

test('walkthrough validation discovers later scripts and requires their screenshots', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-walkthrough-validation-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	const repository = path.join(root, 'repository');
	const output = path.join(root, 'media');
	await writeScript(repository, 'overview-ready');
	await writeScript(repository, 'fresh-project');
	await writeScript(repository, 'added-later');
	await writeArtifacts(output, 'overview-ready');
	await writeArtifacts(output, 'fresh-project');
	await writeArtifacts(output, 'added-later', { missingScreenshot: true });

	await assert.rejects(
		walkthroughArtifacts.validateWalkthroughArtifacts({ repository, output }),
		/Walkthrough 'added-later' is missing screenshot 'steps\/01-overview.png'/,
	);
});

test('walkthrough validation always requires a fresh manifest', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-walkthrough-manifest-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	const repository = path.join(root, 'repository');
	const output = path.join(root, 'media');
	await writeScript(repository, 'overview-ready');
	await writeArtifacts(output, 'overview-ready');
	await rm(path.join(output, 'overview-ready', 'manifest.json'));

	await assert.rejects(
		walkthroughArtifacts.validateWalkthroughArtifacts({ repository, output }),
		/Walkthrough 'overview-ready' is missing manifest.json/,
	);
});

test('release walkthrough validation requires every declared MP4, WebM, and poster', async (t) => {
	for (const asset of ['clip.mp4', 'clip.webm', 'poster.png']) {
		const root = await mkdtemp(path.join(os.tmpdir(), 'motif-walkthrough-release-'));
		t.after(() => rm(root, { recursive: true, force: true }));
		const repository = path.join(root, 'repository');
		const output = path.join(root, 'media');
		await writeScript(repository, 'overview-ready');
		await writeArtifacts(output, 'overview-ready', {
			clip: { webm: 'clip.webm', mp4: 'clip.mp4', webp: 'clip.webp', poster: 'poster.png' },
			missingAsset: asset,
		});

		await assert.rejects(
			walkthroughArtifacts.validateWalkthroughArtifacts({ repository, output, requireVideos: true }),
			(error) => error.message.includes("missing '" + asset + "'"),
		);
	}
});

test('release walkthrough validation requires an available encoder process', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-walkthrough-encoder-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	const repository = path.join(root, 'repository');
	const output = path.join(root, 'media');
	await writeScript(repository, 'overview-ready');
	await writeArtifacts(output, 'overview-ready', {
		clip: { webm: 'clip.webm', mp4: 'clip.mp4', webp: 'clip.webp', poster: 'poster.png' },
	});

	await assert.rejects(
		walkthroughArtifacts.validateWalkthroughArtifacts({
			repository,
			output,
			requireVideos: true,
			ffmpegExecutable: path.join(root, 'missing-ffmpeg.exe'),
		}),
		/Required ffmpeg encoders are unavailable/,
	);
});

test('release walkthrough validation rejects encoder sets missing a required format', () => {
	assert.throws(
		() => walkthroughArtifacts.validateEncoderCapabilities(' V..... libvpx-vp9 VP9 encoder'),
		/ffmpeg is missing required encoders: libx264, libwebp_anim/,
	);
});

async function writeScript(repository, id) {
	const directory = path.join(repository, 'walkthroughs');
	await mkdir(directory, { recursive: true });
	await writeFile(path.join(directory, id + '.walkthrough.json'), JSON.stringify({
		id,
		steps: [{ id: 'overview', kind: 'capture' }],
	}));
}

async function writeArtifacts(output, id, { clip = null, missingScreenshot = false, missingAsset = null } = {}) {
	const directory = path.join(output, id);
	const steps = path.join(directory, 'steps');
	await mkdir(steps, { recursive: true });
	if (!missingScreenshot) await writeFile(path.join(steps, '01-overview.png'), 'png');
	await writeFile(path.join(steps, '01-overview-annotated.png'), 'annotated');
	await writeFile(path.join(directory, 'captions.en.vtt'), 'WEBVTT\n');
	if (clip) {
		for (const file of ['clip.webm', 'clip.mp4', 'clip.webp', 'poster.png']) {
			if (file !== missingAsset) await writeFile(path.join(directory, file), 'media');
		}
	}
	await writeFile(path.join(directory, 'manifest.json'), JSON.stringify({
		id,
		locale: 'en',
		title: id,
		description: 'Walkthrough ' + id + '.',
		width: 1280,
		height: 720,
		fps: 30,
		steps: [{
			id: 'overview',
			caption: 'Overview',
			startMs: 0,
			endMs: 1000,
			screenshot: 'steps/01-overview.png',
			annotated: 'steps/01-overview-annotated.png',
			callouts: [],
		}],
		clip,
	}));
}
