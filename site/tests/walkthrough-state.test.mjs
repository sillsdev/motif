import assert from 'node:assert/strict';
import test from 'node:test';
import { getActiveStepIndex, getNextStepIndex, getSeekTime } from '../src/scripts/walkthrough-state.mjs';

const steps = [
	{ startMs: 0, endMs: 3000 },
	{ startMs: 3000, endMs: 6000 },
];

test('playback time selects the step whose interval contains it', () => {
	assert.equal(getActiveStepIndex(steps, 0), 0);
	assert.equal(getActiveStepIndex(steps, 3), 1);
	assert.equal(getActiveStepIndex(steps, 9), 1);
});

test('selecting and advancing steps use the step start and wrap to the first step', () => {
	assert.equal(getSeekTime(steps, 1), 3);
	assert.equal(getNextStepIndex(steps, 0), 1);
	assert.equal(getNextStepIndex(steps, 1), 0);
});
