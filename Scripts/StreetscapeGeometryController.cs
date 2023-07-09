namespace tichise.StreetscapeGeometry
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    using UnityEngine.XR.ARFoundation;
    using UnityEngine.XR.ARSubsystems;

    using Google.XR.ARCoreExtensions;

#if UNITY_ANDROID

    using UnityEngine.Android;
#endif

    /// <summary>
    /// GeospatialControllerをベースにしたStreetscapeGeometryController用クラス
    /// </summary>
    [SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1118:ParameterMustNotSpanMultipleLines",
        Justification = "Bypass source check.")]
    public class StreetscapeGeometryController : MonoBehaviour
    {
        [Header("AR Components")]

        public ARSessionOrigin SessionOrigin;
        public ARSession Session;
        public ARRaycastManager RaycastManager;
        public AREarthManager EarthManager;
        public ARStreetscapeGeometryManager StreetscapeGeometryManager;
        public ARCoreExtensions ARCoreExtensions;

        /// <summary>
        /// ジオメトリの建物メッシュをレンダリングするためのStreetscapeGeometryマテリアル。
        /// </summary>
        public List<Material> StreetscapeGeometryMaterialBuilding;

        /// <summary>
        /// ジオメトリ地形メッシュをレンダリングするためのStreetscapeGeometryマテリアル。
        /// </summary>
        public Material StreetscapeGeometryMaterialTerrain;

        [Header("Options")]

        /// <summary>
        /// 街路形状を可視化するUI要素。
        /// </summary>
        public Toggle GeometryToggle;

        public Text? InfoText;
        public Text? HelpText;
        public Text? DebugText;


        /// <summary>
        /// Geospatial 機能の初期化中に表示されるヘルプメッセージ。
        /// </summary>
        private const string _localizationInitializingMessage = "Initializing Geospatial functionalities.";

        /// <summary>
        /// EarthTrackingStateがトラッキングしていない、またはポーズ精度がしきい値を超えている場合に表示されるヘルプメッセージ
        /// </summary>
        private const string _localizationInstructionMessage = "Point your camera at buildings, stores, and signs near you.";

        /// <summary>
        /// ロケに失敗したり、タイムアウトになったりしたときに表示されるヘルプメッセージ。
        /// </summary>
        private const string _localizationFailureMessage = "Localization not possible.\n" + "Close and open the app to restart the session.";

        /// <summary>
        /// ローカライズ完了時に表示されるヘルプメッセージ。
        /// </summary>
        private const string _localizationSuccessMessage = "Localization completed.";

        /// <summary>
        /// 解決まで時間がかかった場合に表示されるヘルプメッセージ。
        /// </summary>
        private const string _resolvingTimeoutMessage = "Still resolving the terrain anchor.\n" + "Please make sure you're in an area that has VPS coverage.";

        /// <summary>
        /// ローカリゼーションの完了を待つタイムアウト時間。
        /// </summary>
        private const float _timeoutSeconds = 180;

        /// <summary>
        /// 情報テキストが終了するまでに画面に表示される時間を示す。
        /// </summary>
        private const float _errorDisplaySeconds = 3;

        /// <summary>
        /// 方位ヨーの精度のしきい値
        /// </summary>
        private const double _orientationYawAccuracyThreshold = 25;

        /// <summary>
        /// 定位完了として扱える方位精度の閾値。
        /// </summary>
        private const double _headingAccuracyThreshold = 25;

        /// <summary>
        /// ローカリゼーションとして扱える高度と経度の精度閾値
        /// </summary>
        private const double _horizontalAccuracyThreshold = 20;

        /// <summary>
        /// 街並みのジオメトリがシーンにレンダリングされるかどうか
        /// </summary>
        private bool _streetscapeGeometryVisibility = false;

        /// <summary>
        /// 現在のビルディングメッシュに使用するビルディングマテリアルを決定します。
        /// </summary>
        private int _buildingMatIndex = 0;

        /// <summary>
        /// レンダリング用のオブジェクトをレンダリングするためのstreetcapegeometry辞書。
        /// </summary>
        private Dictionary<TrackableId, GameObject> _streetscapegeometryGOs = new Dictionary<TrackableId, GameObject>();

        /// <summary>
        /// 前回のアップデートで追加されたARStreetscapeGeometries。
        /// </summary>
        List<ARStreetscapeGeometry> _addedStreetscapeGeometrys = new List<ARStreetscapeGeometry>();

        /// <summary>
        /// 最後のアップデートで更新されたARStreetscapeGeometries
        /// </summary>
        List<ARStreetscapeGeometry> _updatedStreetscapeGeometrys = new List<ARStreetscapeGeometry>();

        /// <summary>
        /// 最後のアップデートで削除されたARStreetscapeGeometries。
        /// </summary>
        List<ARStreetscapeGeometry> _removedStreetscapeGeometrys = new List<ARStreetscapeGeometry>();

        /// <summary>
        /// 街並みのジオメトリをシーンから削除するかどうかを決定します。
        /// </summary>
        private bool _clearStreetscapeGeometryRenderObjects = false;

        private bool _waitingForLocationService = false; // ロケーションサービスが開始されたかどうかを確認します。
        private bool _isReturning = false; // セッションがエラーステータスになった場合は、アプリを終了します。
        private bool _isLocalizing = false; // ローカリゼーションが完了したことを示します。
        private bool _enablingGeospatial = false; // ジオメトリの可視化を有効にする必要があるかどうかを決定します。
        private float _localizationPassedTime = 0f; // ローカリゼーションが完了したことを示します。
        private float _configurePrepareTime = 3f;
        private IEnumerator _startLocationService = null;


        /// <summary>
        /// ジオメトリの可視化を有効にする必要があるかどうかを決定します。
        /// </summary>
        /// <param name="enabled"></param>
        public void OnGeometryToggled(bool enabled)
        {
            _streetscapeGeometryVisibility = enabled;
            if (!_streetscapeGeometryVisibility)
            {
                _clearStreetscapeGeometryRenderObjects = true; // 街並みのジオメトリをシーンから削除する必要があることを示します。
            }
        }

        /// <summary>
        /// ジオメトリの可視化を有効にする
        /// </summary>
        public void EnableGeometoryVisualization() {
            OnGeometryToggled(true);
        }

        /// <summary>
        ///  ジオメトリの可視化を無効にする
        /// </summary>
        public void DisableGeometoryVisualization() {
            OnGeometryToggled(false);
        }

        /// <summary>
        /// Unity's Awake() method.
        /// </summary>
        public void Awake()
        {
            // Lock screen to portrait.
            // 画面を縦に固定します。
            /*
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.Portrait;
            */

            // フレームレートを60fpsに設定します。
            Application.targetFrameRate = 60;

            if (SessionOrigin == null)
            {
                Debug.LogError("Cannot find ARSessionOrigin.");
            }

            if (Session == null)
            {
                Debug.LogError("Cannot find ARSession.");
            }

            if (ARCoreExtensions == null)
            {
                Debug.LogError("Cannot find ARCoreExtensions.");
            }
        }


        public void OnEnable()
        {
            _startLocationService = StartLocationService(); // ロケーションサービスを開始します。
            StartCoroutine(_startLocationService); // ロケーションサービスを開始します。

            _isReturning = false;
            _enablingGeospatial = false; 

            SetGeometryToggle(false); // ジオメトリトグルを無効にします。
           
            if (DebugText != null) {
                DebugText.gameObject.SetActive(Debug.isDebugBuild && EarthManager != null);
            }

            if (GeometryToggle != null) {
                GeometryToggle.onValueChanged.AddListener(OnGeometryToggled); // トグルの値が変更されたときに呼び出されるコールバックを登録します。
            }

            _localizationPassedTime = 0f;
            _isLocalizing = true;

            if (StreetscapeGeometryManager == null)
            {
                Debug.LogWarning("StreetscapeGeometryManager must be set in the " +　"GeospatialController Inspector to render StreetscapeGeometry.");
            }

            if (StreetscapeGeometryMaterialBuilding.Count == 0)
            {
                Debug.LogWarning("StreetscapeGeometryMaterialBuilding in the " +　"GeospatialController Inspector must contain at least one material " +　"to render StreetscapeGeometry.");
                return;
            }
        }

        /// <summary>
        /// Unity's OnDisable() method.
        /// </summary>
        public void OnDisable()
        {
            StopCoroutine(_startLocationService); // ロケーションサービスを停止します。
            _startLocationService = null; // ロケーションサービスを停止します。
            Debug.Log("Stop location services.");

            // セッションがトラッキングしているかどうか、位置情報が有効かどうかを確認します。
            Input.location.Stop();
        }

        /// <summary>
        /// Unity's Update() method.
        /// </summary>
        public void Update()
        {
            UpdateDebugInfo();

            // Check session error status.
            LifecycleUpdate();
            if (_isReturning)
            {
                return;
            }

            // セッションがトラッキングしているかどうか、位置情報が有効かどうかを確認します。
            if (ARSession.state != ARSessionState.SessionInitializing &&
                ARSession.state != ARSessionState.SessionTracking)
            {
                return;
            }

            // Geospatial APIがサポートされているかどうかを確認します。
            var featureSupport = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);
            switch (featureSupport)
            {
                case FeatureSupported.Unknown:
                    return;
                case FeatureSupported.Unsupported:
                    // Geospatial APIがサポートされていない場合
                    ReturnWithReason("The Geospatial API is not supported by this device.");
                    return;
                case FeatureSupported.Supported:
                    if (ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode ==
                        GeospatialMode.Disabled)
                    {
                        // Geospatial APIがサポートされているが、無効になっている場合
                        Debug.Log("Geospatial sample switched to GeospatialMode.Enabled.");

                        ARCoreExtensions.ARCoreExtensionsConfig.GeospatialMode =
                            GeospatialMode.Enabled;
                        ARCoreExtensions.ARCoreExtensionsConfig.StreetscapeGeometryMode =
                            StreetscapeGeometryMode.Enabled;
                        _configurePrepareTime = 3.0f;
                        _enablingGeospatial = true;
                        return;
                    }

                    break;
            }

            // Geospatial APIがサポートされている場合
            if (_enablingGeospatial)
            {
                
                _configurePrepareTime -= Time.deltaTime;

                if (_configurePrepareTime < 0)
                {
                    // Geospatial APIがサポートされている場合
                    _enablingGeospatial = false; // ジオメトリの可視化を有効にする必要がないことを示します。
                }
                else
                {
                    return;
                }
            }

            var earthState = EarthManager.EarthState;

            // ジオメトリの可視化を有効にする必要がある場合
            if (earthState == EarthState.ErrorEarthNotReady)
            {
                if (HelpText != null){HelpText.text = _localizationInitializingMessage;}
                return;
            }
            else if (earthState != EarthState.Enabled)
            {
                // Geospatial APIがサポートされているが、無効になっている場合
                string errorMessage = "Geospatial sample encountered an EarthState error: " + earthState;
                Debug.LogWarning(errorMessage);
                if (HelpText != null){HelpText.text = errorMessage;}
                return;
            }

            //  セッションがトラッキングしているかどうか、位置情報が有効かどうかを確認します。
            bool isSessionReady = ARSession.state == ARSessionState.SessionTracking &&
                Input.location.status == LocationServiceStatus.Running;
            var earthTrackingState = EarthManager.EarthTrackingState;
            var pose = earthTrackingState == TrackingState.Tracking ?
                EarthManager.CameraGeospatialPose : new GeospatialPose();
            
            // セッションがトラッキングしていない、またはポーズ精度がしきい値を超えている場合
            if (!isSessionReady || earthTrackingState != TrackingState.Tracking ||
                pose.OrientationYawAccuracy > _orientationYawAccuracyThreshold ||
                pose.HorizontalAccuracy > _horizontalAccuracyThreshold)
            {
                // セッションがトラッキングしていない、またはポーズ精度がしきい値を超えている場合
                if (!_isLocalizing)
                {
                    _isLocalizing = true;
                    _localizationPassedTime = 0f;

                    // 街並みのジオメトリをシーンから削除する必要がある場合
                    SetGeometryToggle(false);
                }

                if (_localizationPassedTime > _timeoutSeconds)
                {
                    // ローカリゼーションがタイムアウトした場合
                    Debug.LogError("Geospatial sample localization timed out.");

                    // ローカリゼーションがタイムアウトしたことを示します。
                    ReturnWithReason(_localizationFailureMessage);
                }
                else
                {
                    // セッションがトラッキングしていない、またはポーズ精度がしきい値を超えている場合
                    _localizationPassedTime += Time.deltaTime;

                    if (HelpText != null)
                    {
                        HelpText.text = _localizationInstructionMessage;
                    }
                }
            }
            else if (_isLocalizing)
            {
                // セッションがトラッキングしている、かつポーズ精度がしきい値を超えていない場合

                // ローカリゼーションが完了したことを示します。
                _isLocalizing = false;

                // ローカリゼーションが完了したことを示します。
                _localizationPassedTime = 0f;

                // 街並みのジオメトリをシーンに追加する必要がある場合
                SetGeometryToggle(true);

                if (HelpText != null) {
                    HelpText.text = _localizationSuccessMessage;
                }
            }
            else
            {
                // セッションがトラッキングしている、かつポーズ精度がしきい値を超えていない場合
                if (_streetscapeGeometryVisibility)
                {
                    // 街並みのジオメトリをシーンに追加する必要がある場合
                    if (StreetscapeGeometryManager)
                    {
                        // ARStreetscapeGeometryManagerのARStreetscapeGeometriesChangedイベントを登録します。
                        StreetscapeGeometryManager.StreetscapeGeometriesChanged += (ARStreetscapeGeometrysChangedEventArgs) =>
                        {
                            // 前回のアップデートで追加されたARStreetscapeGeometriesを取得します。
                            _addedStreetscapeGeometrys = ARStreetscapeGeometrysChangedEventArgs.Added;

                            // 前回のアップデートで更新されたARStreetscapeGeometriesを取得します。
                            _updatedStreetscapeGeometrys = ARStreetscapeGeometrysChangedEventArgs.Updated;

                            // 前回のアップデートで削除されたARStreetscapeGeometriesを取得します。
                            _removedStreetscapeGeometrys = ARStreetscapeGeometrysChangedEventArgs.Removed;
                        };
                    }

                    // 街並みのジオメトリをシーンに追加する必要がある場合
                    foreach (ARStreetscapeGeometry streetscapegeometry in _addedStreetscapeGeometrys)
                    {
                        // 街並みのジオメトリをシーンに追加します。
                        InstantiateRenderObject(streetscapegeometry);
                    }

                    foreach (ARStreetscapeGeometry streetscapegeometry in _updatedStreetscapeGeometrys) {
                        // 街並みのジオメトリをシーンに追加する必要がある場合
                        InstantiateRenderObject(streetscapegeometry);

                        // 街並みのジオメトリをシーンに追加する必要がない場合
                        UpdateRenderObject(streetscapegeometry);
                    }

                    // 街並みのジオメトリをシーンから削除する必要がある場合
                    foreach (ARStreetscapeGeometry streetscapegeometry in _removedStreetscapeGeometrys)
                    {
                        // 街並みのジオメトリをシーンから削除します。
                        DestroyRenderObject(streetscapegeometry);
                    }
                }
                else if (_clearStreetscapeGeometryRenderObjects)
                {
                    // 街並みのジオメトリをシーンから削除します。
                    DestroyAllRenderObjects();

                    // 街並みのジオメトリをシーンから削除する必要がないことを示します。
                    _clearStreetscapeGeometryRenderObjects = false;
                }
            }

            // セッションがトラッキングしている場合
            if (earthTrackingState == TrackingState.Tracking)
            {
                // InfoTextが存在する場合
                if (InfoText != null)
                {
                    InfoText.text = string.Format(
                    "Latitude/Longitude: {1}°, {2}°{0}" +
                    "Horizontal Accuracy: {3}m{0}" +
                    "Altitude: {4}m{0}" +
                    "Vertical Accuracy: {5}m{0}" +
                    "Eun Rotation: {6}{0}" +
                    "Orientation Yaw Accuracy: {7}°",
                    Environment.NewLine,
                    pose.Latitude.ToString("F6"),
                    pose.Longitude.ToString("F6"),
                    pose.HorizontalAccuracy.ToString("F6"),
                    pose.Altitude.ToString("F2"),
                    pose.VerticalAccuracy.ToString("F2"),
                    pose.EunRotation.ToString("F1"),
                    pose.OrientationYawAccuracy.ToString("F1"));
                }
                
            }
            else
            {
                // InfoTextが存在する場合
                if (InfoText != null){InfoText.text = "GEOSPATIAL POSE: not tracking";}
            }
        }

        /// <summary>
        /// ARStreetscapeGeometryのレンダーオブジェクトを設定します。
        /// </summary>
        private void InstantiateRenderObject(ARStreetscapeGeometry streetscapegeometry)
        {
            if (streetscapegeometry.mesh == null)
            {
                return;
            }

            // すでにレンダーオブジェクトが存在する場合は、何もしません。
            if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
            {
                return;
            }

            // レンダーオブジェクトを作成します。
            GameObject renderObject = new GameObject(
                "StreetscapeGeometryMesh", typeof(MeshFilter), typeof(MeshRenderer));

            // レンダーオブジェクトを設定します。
            if (renderObject)
            {
                // レンダーオブジェクトの位置を設定します。
                renderObject.transform.position = new Vector3(0, 0.5f, 0);

                // レンダーオブジェクトのメッシュを設定します。
                renderObject.GetComponent<MeshFilter>().mesh = streetscapegeometry.mesh;

                // castShadowを無効にします。
                renderObject.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // レンダーオブジェクトのマテリアルを設定します。
                if (streetscapegeometry.streetscapeGeometryType == StreetscapeGeometryType.Building)
                {
                    // ビルディングメッシュの場合

                    // ビルディングマテリアルを設定します。
                    renderObject.GetComponent<MeshRenderer>().material = StreetscapeGeometryMaterialBuilding[_buildingMatIndex];

                    // ビルディングマテリアルのインデックスを更新します。
                    _buildingMatIndex = (_buildingMatIndex + 1) % StreetscapeGeometryMaterialBuilding.Count;
                }
                else
                {
                    //  地形メッシュの場合
                    renderObject.GetComponent<MeshRenderer>().material = StreetscapeGeometryMaterialTerrain; 
                }

                // レンダーオブジェクトの位置と回転を設定します。
                renderObject.transform.position = streetscapegeometry.pose.position;
                renderObject.transform.rotation = streetscapegeometry.pose.rotation;

                // レンダーオブジェクトを追加します。
                _streetscapegeometryGOs.Add(streetscapegeometry.trackableId, renderObject);
            }
        }

        /// <summary>
        /// このstreetcapegeometrysのポーズに基づいてレンダーオブジェクトのトランスフォームを更新します。
        /// メッシュを更新するために毎フレーム呼び出す必要があります。
        /// </summary>
        private void UpdateRenderObject(ARStreetscapeGeometry streetscapegeometry)
        {
            // レンダーオブジェクトが存在しない場合は、何もしません。
            if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
            {
                // レンダーオブジェクトの位置と回転を更新します。
                GameObject renderObject = _streetscapegeometryGOs[streetscapegeometry.trackableId];

                // レンダーオブジェクトの位置を設定します。
                renderObject.transform.position = streetscapegeometry.pose.position;

                // レンダーオブジェクトの回転を設定します。
                renderObject.transform.rotation = streetscapegeometry.pose.rotation;
            }
        }

        /// <summary>
        ///  レンダーオブジェクトを削除します。
        /// </summary>
        /// <param name="streetscapegeometry"></param>
        private void DestroyRenderObject(ARStreetscapeGeometry streetscapegeometry)
        {
            if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
            {
                // レンダーオブジェクトを削除します。
                var geometry = _streetscapegeometryGOs[streetscapegeometry.trackableId];
                _streetscapegeometryGOs.Remove(streetscapegeometry.trackableId);
                Destroy(geometry);
            }
        }

        /// <summary>
        /// すべてのレンダーオブジェクトを削除します。
        /// </summary>
        private void DestroyAllRenderObjects()
        {
            var keys = _streetscapegeometryGOs.Keys;
            foreach (var key in keys)
            {
                // レンダーオブジェクトを削除します。
                var renderObject = _streetscapegeometryGOs[key];
                Destroy(renderObject);
            }

            // レンダーオブジェクトのリストをクリアします。
            _streetscapegeometryGOs.Clear();
        }

        /// <summary>
        ///  ロケーションサービスを開始します。
        /// </summary>
        /// <returns></returns>
        private IEnumerator StartLocationService()
        {
            // ロケーションサービスが有効になっているかどうかを確認します。
            _waitingForLocationService = true;
#if UNITY_ANDROID
            // Androidの場合、ユーザーにファインロケーションの許可を求めます。
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                Debug.Log("Requesting the fine location permission.");
                Permission.RequestUserPermission(Permission.FineLocation);
                yield return new WaitForSeconds(3.0f);
            }
#endif

            //  ロケーションサービスが有効になっているかどうかを確認します。
            if (!Input.location.isEnabledByUser)
            {
                Debug.Log("Location service is disabled by the user.");
                _waitingForLocationService = false;
                yield break;
            }

            Debug.Log("Starting location service.");

            // ロケーションサービスを開始します。
            Input.location.Start();

            // ロケーションサービスが開始されるまで待ちます。
            while (Input.location.status == LocationServiceStatus.Initializing)
            {
                // 1秒待ちます。
                yield return null;
            }

            _waitingForLocationService = false; // ロケーションサービスが開始されたことを示します。

            // ロケーションサービスが開始されたかどうかを確認します。
            if (Input.location.status != LocationServiceStatus.Running)
            {
                Debug.LogWarningFormat("Location service ended with {0} status.", Input.location.status);

                // ロケーションサービスが開始されなかった場合は、ロケーションサービスを停止します。
                Input.location.Stop();
            }
        }

        /// <summary>
        ///  セッションのエラーステータスを確認します。
        /// </summary>
        private void LifecycleUpdate()
        {
            // セッションがエラーステータスになった場合は、アプリを終了します。
            if (Input.GetKeyUp(KeyCode.Escape))
            {
                Application.Quit();
            }

            if (_isReturning)
            {
                return;
            }

            // セッションがトラッキングしていない場合は、画面のスリープを無効にします。
            var sleepTimeout = SleepTimeout.NeverSleep;

            // セッションがトラッキングしていない場合は、画面のスリープを無効にします。
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                sleepTimeout = SleepTimeout.SystemSetting;
            }

            // 画面のスリープを設定します。
            Screen.sleepTimeout = sleepTimeout;

            // セッションがエラーステータスになった場合は、アプリを終了します。
            string returningReason = string.Empty;

            // セッションがエラーステータスになった場合は、アプリを終了します。
            if (ARSession.state != ARSessionState.CheckingAvailability &&
                ARSession.state != ARSessionState.Ready &&
                ARSession.state != ARSessionState.SessionInitializing &&
                ARSession.state != ARSessionState.SessionTracking)
            {
                // セッションがエラーステータスになった場合は、アプリを終了します。
                returningReason = string.Format(
                    "Geospatial sample encountered an ARSession error state {0}.\n" +
                    "Please restart the app.",
                    ARSession.state);
            }
            else if (Input.location.status == LocationServiceStatus.Failed)
            {
                // ロケーションサービスが失敗した場合は、アプリを終了します。
                returningReason =
                    "Geospatial sample failed to start location service.\n" +
                    "Please restart the app and grant the fine location permission.";
            }
            else if (SessionOrigin == null || Session == null || ARCoreExtensions == null)
            {
                // ARCoreExtensionsが無効になっている場合は、アプリを終了します。
                returningReason = string.Format(
                    "Geospatial sample failed due to missing AR Components.");
            }

            // エラーが発生した場合は、アプリを終了します。
            ReturnWithReason(returningReason);
        }

        /// <summary>
        /// エラーが発生した場合は、アプリを終了します。
        /// </summary>
        /// <param name="reason"></param>
        private void ReturnWithReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return;
            }

            SetGeometryToggle(false);

            Debug.LogError(reason);

            if (HelpText != null)
            {
                // ヘルプテキストを設定します。
                HelpText.text = reason;
            }
            _isReturning = true;

            // エラーが発生した場合は、アプリを終了します。
            Invoke(nameof(QuitApplication), _errorDisplaySeconds);
        }

        /// <summary>
        /// アプリを終了します。
        /// </summary>
        private void QuitApplication()
        {
            Application.Quit();
        }

        /// <summary>
        /// デバッグ情報を更新します。
        /// </summary>
        private void UpdateDebugInfo()
        {
            if (!Debug.isDebugBuild || EarthManager == null)
            {
                return;
            }

            var pose = EarthManager.EarthState == EarthState.Enabled &&
                EarthManager.EarthTrackingState == TrackingState.Tracking ?
                EarthManager.CameraGeospatialPose : new GeospatialPose();
            var supported = EarthManager.IsGeospatialModeSupported(GeospatialMode.Enabled);

            if (DebugText == null)
            {
                return;
            }

            // デバッグテキストを設定します。
            DebugText.text =
                $"IsReturning: {_isReturning}\n" +
                $"IsLocalizing: {_isLocalizing}\n" +
                $"SessionState: {ARSession.state}\n" +
                $"LocationServiceStatus: {Input.location.status}\n" +
                $"FeatureSupported: {supported}\n" +
                $"EarthState: {EarthManager.EarthState}\n" +
                $"EarthTrackingState: {EarthManager.EarthTrackingState}\n" +
                $"  LAT/LNG: {pose.Latitude:F6}, {pose.Longitude:F6}\n" +
                $"  HorizontalAcc: {pose.HorizontalAccuracy:F6}\n" +
                $"  ALT: {pose.Altitude:F2}\n" +
                $"  VerticalAcc: {pose.VerticalAccuracy:F2}\n" +
                $". EunRotation: {pose.EunRotation:F2}\n" +
                $"  OrientationYawAcc: {pose.OrientationYawAccuracy:F2}";
        }


        // GeometryToggleのActiveを切り替える
        public void SetGeometryToggle(bool ative) {
            if (GeometryToggle != null) {
                GeometryToggle.gameObject.SetActive(ative); // ジオメトリトグルを無効にします。
            }
        }

        /// <summary>
        /// 街並みのジオメトリを更新します。
        /// </summary>
        public void UpdateMaterials () {
            foreach (ARStreetscapeGeometry streetscapegeometry in _updatedStreetscapeGeometrys) {
                // 街並みのジオメトリをシーンに追加する必要がない場合
                
                // レンダーオブジェクトが存在しない場合は、何もしません。
                if (_streetscapegeometryGOs.ContainsKey(streetscapegeometry.trackableId))
                {
                    // レンダーオブジェクトの位置と回転を更新します。
                    GameObject renderObject = _streetscapegeometryGOs[streetscapegeometry.trackableId];

                    // レンダーオブジェクトのマテリアルを設定します。
                    if (streetscapegeometry.streetscapeGeometryType == StreetscapeGeometryType.Building)
                    {
                        // ビルディングマテリアルを設定します。
                        renderObject.GetComponent<MeshRenderer>().material = StreetscapeGeometryMaterialBuilding[0];
                    }
                }
            }
        }
    }
}
