// Copyright 2022 Robotec.ai.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Object = System.Object;

namespace RGLUnityPlugin
{
    /// <summary>
    /// Encapsulates all non-ROS components of a RGL-based Lidar.
    /// </summary>
    [RequireComponent(typeof(PointCloudVisualization))]
    public class LidarSensor : MonoBehaviour
    {
        /// <summary>
        /// This data is output from LidarSensor at the OutputHz cycle.
        /// </summary>
        public struct OutputData
        {
            /// <summary>
            /// Number of rays that actually has hit anything.
            /// </summary>
            public int hitCount;

            /// <summary>
            /// Vertices for visualization, Unity coordinate frame.
            /// </summary>
            public Vector3[] hits;

            /// <summary>
            /// Vertices for publishing Autoware format pointcloud, ROS coordinate frame
            /// </summary>
            public byte[] rosPCL24;

            /// <summary>
            /// Vertices for publishing extended Autoware format pointcloud, ROS coordinate frame
            /// </summary>
            public byte[] rosPCL48;
        }

        /// <summary>
        /// Sensor processing and callbacks are automatically called in this hz.
        /// </summary>
        [FormerlySerializedAs("OutputHz")]
        [Range(0, 50)] public int AutomaticCaptureHz = 10;

        /// <summary>
        /// Delegate used in callbacks.
        /// </summary>
        /// <param name="outputData">Data output for each hz</param>
        public delegate void OnOutputDataDelegate(OutputData outputData);

        /// <summary>
        /// Called when new data is generated via automatic capture.
        /// </summary>
        public OnOutputDataDelegate OnOutputData;

        /// <summary>
        /// Allows to select one of built-in LiDAR models.
        /// Defaults to a range meter to ensure the choice is conscious.
        /// </summary>
        public LidarModel modelPreset = LidarModel.RangeMeter;

        /// <summary>
        /// Allows to quickly enable/disable gaussian noise.
        /// </summary>
        public bool applyGaussianNoise = true;

        /// <summary>
        /// Encapsulates description of a point cloud generated by a LiDAR and allows for fine-tuning.
        /// </summary>
        public LidarConfiguration configuration = LidarConfigurationLibrary.ByModel[LidarModel.RangeMeter];

        private RGLLidar rglLidar;
        private SceneManager sceneManager;
        private PointCloudVisualization pointCloudVisualization;
        private OutputData outputData;
        private LidarModel? validatedPreset;
        private float timer;

        public void Start()
        {
            sceneManager = FindObjectOfType<SceneManager>();
            
            if (sceneManager == null)
            {
                // TODO(prybicki): this is too tedious, implement automatic instantiation of RGL Scene Manager
                Debug.LogError($"RGL Scene Manager is not present on the scene. Destroying {name}.");
                Destroy(this);
            }

            OnValidate();
            if (rglLidar == null)
            {
                ApplyConfiguration(configuration);
            }
        }

        public void OnValidate()
        {
            bool presetChanged = validatedPreset != modelPreset;
            bool firstValidation = validatedPreset == null;
            if (!firstValidation && presetChanged)
            {
                configuration = LidarConfigurationLibrary.ByModel[modelPreset];
            }

            ApplyConfiguration(configuration);
            validatedPreset = modelPreset;
        }

        private void ApplyConfiguration(LidarConfiguration newConfig)
        {
            rglLidar = null; // When GetRayPoses() fails, having null rglLidar will result in more readable error.
            rglLidar = new RGLLidar(newConfig.GetRayPoses(), newConfig.laserArray.GetLaserRingIds());
            rglLidar.SetGaussianNoiseParamsCtx(applyGaussianNoise
                ? newConfig.noiseParams
                : LidarConfiguration.ZeroNoiseParams);
            outputData = new OutputData
            {
                hits = new Vector3[newConfig.PointCloudSize],
                rosPCL24 = new byte[24 * newConfig.PointCloudSize],
                rosPCL48 = new byte[48 * newConfig.PointCloudSize]
            };
        }

        public void FixedUpdate()
        {
            if (AutomaticCaptureHz == 0.0f)
            {
                return;
            }
            
            timer += Time.deltaTime;

            var interval = 1.0f / AutomaticCaptureHz;
            if (timer + 0.00001f < interval)
                return;
            timer = 0;

            OnOutputData.Invoke(RequestCapture());
        }

        public OutputData RequestCapture()
        {
            sceneManager.DoUpdate();
            
            rglLidar.RaytraceAsync(
                transform.localToWorldMatrix,
                ROS2.Transformations.Unity2RosMatrix4x4() * transform.worldToLocalMatrix,
                configuration.maxRange);
            
            rglLidar.SyncAndDownload(
                ref outputData.hitCount,
                ref outputData.hits,
                ref outputData.rosPCL24,
                ref outputData.rosPCL48);

            Vector3[] onlyHits = new Vector3[outputData.hitCount];
            Array.Copy(outputData.hits, onlyHits, outputData.hitCount);
            GetComponent<PointCloudVisualization>().SetPoints(onlyHits);

            return outputData;
        }
    }
}