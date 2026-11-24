using UnityEngine;

namespace Flopyard
{
    // Adapted from sergioabreu-g/active-ragdolls (Apache-2.0), ConfigurableJointExtensions
    // by Michael Stevenson: turns a wanted local rotation into a ConfigurableJoint target rotation.
    public static class JointTargets
    {
        public static void SetTargetLocal(ConfigurableJoint joint, Quaternion targetLocal, Quaternion startLocal)
        {
            var right = joint.axis;
            var forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJoint = Quaternion.LookRotation(forward, up);
            Quaternion r = Quaternion.Inverse(worldToJoint);
            r *= Quaternion.Inverse(targetLocal) * startLocal;
            r *= worldToJoint;
            joint.targetRotation = r;
        }
    }
}
