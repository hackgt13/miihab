// Image-space prerequisites only; these do not establish measurement accuracy.
export const FRAMING_RULE = Object.freeze({
  version: 'kinesthetic.framing.v1',
  minimumVisibility: 0.5,
  requireInsideImage: true,
});

const JOINT_NAMES = {
  11: 'left shoulder', 12: 'right shoulder',
  13: 'left elbow', 14: 'right elbow',
  15: 'left wrist', 16: 'right wrist',
  23: 'left hip', 24: 'right hip',
};

export const EXERCISE_VIEWS = Object.freeze([
  { id: 'leftShoulderRaise', label: 'Left shoulder raise', joints: [11, 13, 23] },
  { id: 'rightShoulderRaise', label: 'Right shoulder raise', joints: [12, 14, 24] },
  { id: 'leftElbowBend', label: 'Left elbow bend', joints: [11, 13, 15] },
  { id: 'rightElbowBend', label: 'Right elbow bend', joints: [12, 14, 16] },
]);

export function jointIssue(point) {
  if (!point || !Number.isFinite(point.x) || !Number.isFinite(point.y)) return 'missing';
  if (point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1) return 'outside image';
  if (!Number.isFinite(point.visibility) || point.visibility < FRAMING_RULE.minimumVisibility) return 'low visibility';
  return null;
}

export function assessFraming(landmarks = []) {
  return Object.fromEntries(EXERCISE_VIEWS.map(view => {
    const missing = view.joints.flatMap(index => {
      const reason = jointIssue(landmarks[index]);
      return reason ? [{ index, joint: JOINT_NAMES[index], reason }] : [];
    });
    return [view.id, { jointsInView: missing.length === 0, missing }];
  }));
}
