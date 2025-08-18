/****************************************************************************
ABB Joint Limit Monitor - Monitors joint limits and warnings
****************************************************************************/

using System;
using UnityEngine;
using Preliy.Flange;

namespace ABB.RWS
{
    [Serializable]
    public class JointLimitStatus
    {
        public bool[] limitWarnings = new bool[6];
        public float[] limitPercentages = new float[6];
        public float[] minLimits = new float[6];
        public float[] maxLimits = new float[6];
        public string overallStatus = "OK";
    }

    public class ABBJointLimitMonitor
    {
        private readonly Controller flangeController;
        private readonly bool enableDebugLogging;
        private readonly JointLimitStatus status = new JointLimitStatus();
        private readonly float limitWarningThreshold = 0.05f; // 5% of range

        // Events
        public event Action<int, bool> OnJointLimitWarning; // jointIndex, isWarning

        public ABBJointLimitMonitor(Controller flangeController, bool enableDebugLogging = false)
        {
            this.flangeController = flangeController ?? throw new ArgumentNullException(nameof(flangeController));
            this.enableDebugLogging = enableDebugLogging;
        }

        public JointLimitStatus CurrentStatus => status;

        public void CheckJointLimits(float[] jointAngles)
        {
            if (flangeController?.MechanicalGroup?.RobotJoints == null || jointAngles == null) 
                return;

            bool anyLimitWarning = false;

            for (int i = 0; i < System.Math.Min(jointAngles.Length, flangeController.MechanicalGroup.RobotJoints.Count); i++)
            {
                var joint = flangeController.MechanicalGroup.RobotJoints[i];
                if (joint?.Config == null) continue;

                float minLimit = joint.Config.Limits.x;
                float maxLimit = joint.Config.Limits.y;
                float currentAngle = jointAngles[i];

                // Store limit values
                status.minLimits[i] = minLimit;
                status.maxLimits[i] = maxLimit;

                // Calculate percentage of limit range used
                float range = maxLimit - minLimit;
                float position = currentAngle - minLimit;
                float percentage = (position / range) * 100f;
                status.limitPercentages[i] = Mathf.Abs(percentage);

                // Check if approaching limits
                bool wasWarning = status.limitWarnings[i];
                bool isWarning = (currentAngle <= minLimit + (range * limitWarningThreshold)) || 
                               (currentAngle >= maxLimit - (range * limitWarningThreshold));

                status.limitWarnings[i] = isWarning;

                if (isWarning != wasWarning)
                {
                    OnJointLimitWarning?.Invoke(i, isWarning);
                    if (isWarning && enableDebugLogging)
                    {
                        LogWarning($"Joint {i + 1} approaching limit: {currentAngle:F2}° (range: {minLimit:F2}° to {maxLimit:F2}°)");
                    }
                }

                if (isWarning) anyLimitWarning = true;
            }

            string newStatus = anyLimitWarning ? "WARNING - Joint limits approached!" : "OK";
            if (newStatus != status.overallStatus)
            {
                status.overallStatus = newStatus;
                if (anyLimitWarning && enableDebugLogging)
                {
                    LogWarning($"Joint limit status: {status.overallStatus}");
                }
            }
        }

        public bool IsJointInWarning(int jointIndex)
        {
            return jointIndex >= 0 && jointIndex < status.limitWarnings.Length && status.limitWarnings[jointIndex];
        }

        public bool HasAnyWarnings()
        {
            for (int i = 0; i < status.limitWarnings.Length; i++)
            {
                if (status.limitWarnings[i]) return true;
            }
            return false;
        }

        public float GetJointLimitUsagePercent(int jointIndex)
        {
            if (jointIndex >= 0 && jointIndex < status.limitPercentages.Length)
                return status.limitPercentages[jointIndex];
            return 0f;
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogging)
                Debug.LogWarning($"[ABB Joint Limit Monitor] {message}");
        }
    }
}