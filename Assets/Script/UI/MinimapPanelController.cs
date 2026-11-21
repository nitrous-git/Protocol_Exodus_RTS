using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Image = UnityEngine.UI.Image;
using RawImage = UnityEngine.UI.RawImage;
using RectMask2D = UnityEngine.UI.RectMask2D;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class MinimapPanelController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IInitializePotentialDragHandler, IDragHandler
{
    [Header("Terrain")]
    [SerializeField] private RectTransform mapArea;
    [SerializeField, Range(128, 2048)] private int captureResolution = 512;
    [Tooltip("Index of the lightweight renderer in the URP asset. Falls back to the default renderer if absent.")]
    [SerializeField, Min(-1)] private int terrainRendererIndex = 1;

    [Header("Markers")]
    [SerializeField, Min(0.02f)] private float markerUpdateInterval = 0.1f;
    [SerializeField, Min(1f)] private float unitMarkerSize = 4f;
    [SerializeField, Min(1f)] private float minimumBuildingMarkerSize = 7f;
    [SerializeField, Min(1f)] private float mineralMarkerSize = 4f;
    [SerializeField] private Color friendlyColor = new(0.28f, 0.66f, 1f);
    [SerializeField] private Color enemyColor = new(1f, 0.32f, 0.30f);
    [SerializeField] private Color mineralColor = new(1f, 0.81f, 0.27f);

    [Header("Viewport")]
    [SerializeField, Min(1f)] private float viewportLineWidth = 1.5f;
    [SerializeField] private Color viewportColor = new(0.96f, 0.97f, 1f, 0.93f);

    private const float FrameWidth = 2f;
    private static readonly Vector2[] ViewportCorners =
    {
        Vector2.zero, Vector2.right, Vector2.one, Vector2.up
    };

    private readonly Vector2[] outlineCorners = new Vector2[4];
    private readonly Image[] outlineEdges = new Image[4];

    private GameContext gameContext;
    private CameraController cameraController;
    private Camera gameplayCamera;
    private Terrain terrain;
    private TerrainCollider terrainCollider;
    private Vector3 terrainOrigin;
    private Vector3 terrainSize;

    private Camera captureCamera;
    private RenderTexture terrainTexture;
    private UniversalRenderPipeline.SingleCameraRequest captureRequest;
    private bool terrainCaptured;

    private Image frame;
    private RawImage terrainImage;
    private MarkerPool resources;
    private MarkerPool buildings;
    private MarkerPool units;
    private float timeUntilMarkerUpdate;
    private bool hasViewportProjection;
    private Matrix4x4 lastViewMatrix;
    private Matrix4x4 lastProjectionMatrix;
    private int? navigationPointerId;

    public void Initialize(GameContext gameContext, MatchWorld matchWorld)
    {
        this.gameContext = gameContext;
        cameraController = matchWorld != null ? matchWorld.CameraController : null;
        gameplayCamera = cameraController != null ? cameraController.GetComponentInChildren<Camera>() : null;
        terrain = cameraController != null ? cameraController.Terrain : null;
        ReleaseCapture();
        timeUntilMarkerUpdate = 0f;
        hasViewportProjection = false;
        navigationPointerId = null;

        bool hasTerrain = terrain != null && terrain.terrainData != null;
        if (!hasTerrain)
        {
            if (frame != null)
                frame.gameObject.SetActive(false);
            return;
        }

        terrainOrigin = terrain.transform.position;
        terrainSize = terrain.terrainData.size;
        terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (frame == null)
            CreateView();

        frame.gameObject.SetActive(isActiveAndEnabled);
    }

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || gameContext == null || terrain == null || frame == null)
            return;

        CaptureTerrainIfNeeded();
        UpdateMapLayout();
        if (terrainImage.rectTransform.rect.width <= 0f || terrainImage.rectTransform.rect.height <= 0f)
            return;

        timeUntilMarkerUpdate -= deltaTime;
        if (timeUntilMarkerUpdate <= 0f)
        {
            UpdateMarkers();
            timeUntilMarkerUpdate = markerUpdateInterval;
        }

        UpdateViewport();
    }

    private void CreateView()
    {
        // The existing HUD uses this inset as the placeholder in all match scenes.
        RectTransform area = mapArea != null ? mapArea : transform.Find("Content/PlaceholderImage") as RectTransform;
        if (area == null)
            area = (RectTransform)transform;

        frame = CreateViewObject<Image>("Minimap Frame", area);
        StretchToParent(frame.rectTransform);
        frame.color = new Color32(18, 24, 29, 255);
        frame.raycastTarget = true;

        terrainImage = CreateViewObject<RawImage>("Terrain Image", frame.rectTransform);
        terrainImage.enabled = false;
        terrainImage.gameObject.AddComponent<RectMask2D>();

        resources = new MarkerPool("Minerals", terrainImage.rectTransform);
        buildings = new MarkerPool("Buildings", terrainImage.rectTransform);
        units = new MarkerPool("Units", terrainImage.rectTransform);

        RectTransform outline = CreateLayer("Viewport Outline", terrainImage.rectTransform);
        for (int i = 0; i < outlineEdges.Length; i++)
        {
            outlineEdges[i] = CreateViewObject<Image>("Edge", outline);
            outlineEdges[i].color = viewportColor;
        }
    }

    private void UpdateMapLayout()
    {
        Vector2 available = Vector2.Max(frame.rectTransform.rect.size - Vector2.one * (2f * FrameWidth), Vector2.zero);
        float scale = Mathf.Min(available.x / terrainSize.x, available.y / terrainSize.z);
        Vector2 fittedSize = new(terrainSize.x * scale, terrainSize.z * scale);
        RectTransform map = terrainImage.rectTransform;
        if (map.sizeDelta == fittedSize)
            return;

        map.sizeDelta = fittedSize;
        timeUntilMarkerUpdate = 0f;
        hasViewportProjection = false;
    }

    private void CaptureTerrainIfNeeded()
    {
        if (!terrain.isActiveAndEnabled || (terrainCaptured && terrainTexture.IsCreated()) ||
            GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline)
            return;

        if (captureCamera == null)
        {
            float scale = (float)captureResolution / Mathf.Max(terrainSize.x, terrainSize.z);
            terrainTexture = new RenderTexture(
                Mathf.Max(1, Mathf.RoundToInt(terrainSize.x * scale)),
                Mathf.Max(1, Mathf.RoundToInt(terrainSize.z * scale)), 24, RenderTextureFormat.ARGB32)
            {
                name = "Minimap Terrain",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            GameObject cameraObject = new("Minimap Capture Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            captureCamera = cameraObject.GetComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.orthographicSize = terrainSize.z * 0.5f;
            captureCamera.aspect = terrainSize.x / terrainSize.z;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = frame.color;
            // Match terrains use a dedicated layer. Gameplay objects and effects use other layers.
            captureCamera.cullingMask = 1 << terrain.gameObject.layer;
            captureCamera.allowHDR = false;
            captureCamera.allowMSAA = false;
            captureCamera.allowDynamicResolution = false;
            captureCamera.useOcclusionCulling = false;
            captureCamera.nearClipPlane = 0.1f;
            captureCamera.farClipPlane = terrainSize.y + 2f;
            captureCamera.transform.SetPositionAndRotation(
                terrainOrigin + new Vector3(terrainSize.x * 0.5f, terrainSize.y + 1f, terrainSize.z * 0.5f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));

            UniversalAdditionalCameraData cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;
            cameraData.allowXRRendering = false;
            cameraData.volumeLayerMask = 0;
            cameraData.requiresColorOption = CameraOverrideOption.Off;
            cameraData.requiresDepthOption = CameraOverrideOption.Off;
            if (terrainRendererIndex >= 0 && terrainRendererIndex < pipeline.rendererDataList.Length &&
                pipeline.rendererDataList[terrainRendererIndex] != null)
                cameraData.SetRenderer(terrainRendererIndex);

            captureRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = terrainTexture };
        }

        if (!RenderPipeline.SupportsRenderRequest(captureCamera, captureRequest))
            return;

        RenderPipeline.SubmitRenderRequest(captureCamera, captureRequest);
        terrainCaptured = terrainTexture.IsCreated();
        terrainImage.texture = terrainTexture;
        terrainImage.enabled = terrainCaptured;
    }

    private void UpdateMarkers()
    {
        float cellSize = gameContext.TerrainGrid?.CellSize ?? 0f;
        Vector2 mapSize = terrainImage.rectTransform.rect.size;
        float unitsPerWorldUnit = mapSize.x / terrainSize.x;
        IReadOnlyList<ResourceNode> allResources = gameContext.AllResourceNodes;
        for (int i = 0; i < allResources.Count; i++)
        {
            ResourceNode node = allResources[i];
            if (node == null || !node.isActiveAndEnabled || !node.IsInitialized || node.IsDepleted ||
                node.ResourceType != ResourceType.Minerals)
                continue;

            resources.Add(WorldToNormalized(node.Position), Vector2.one * mineralMarkerSize, mineralColor, 45f);
        }

        IReadOnlyList<BuildingBase> allBuildings = gameContext.AllBuildings;
        for (int i = 0; i < allBuildings.Count; i++)
        {
            BuildingBase building = allBuildings[i];
            if (building == null || !building.isActiveAndEnabled || !building.IsInitialized || !building.IsAlive)
                continue;

            // The placed footprint is already rotated into world X/Z by the placement system.
            Vector2 footprint = Vector2.Max(building.PlacedFootprintSize, Vector2.one);
            float scale = Mathf.Max(cellSize * unitsPerWorldUnit,
                minimumBuildingMarkerSize / Mathf.Max(footprint.x, footprint.y));
            buildings.Add(WorldToNormalized(building.Position), footprint * scale, FactionColor(building.OwnerFaction));
        }

        IReadOnlyList<UnitBase> allUnits = gameContext.AllUnits;
        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase unit = allUnits[i];
            if (unit == null || !unit.isActiveAndEnabled || !unit.IsInitialized || !unit.IsAlive)
                continue;

            units.Add(WorldToNormalized(unit.Position), Vector2.one * unitMarkerSize, FactionColor(unit.OwnerFaction));
        }

        resources.FinishUpdate();
        buildings.FinishUpdate();
        units.FinishUpdate();
    }

    private Color FactionColor(Faction faction) => faction == gameContext.PlayerFaction ? friendlyColor : enemyColor;

    private Vector2 WorldToNormalized(Vector3 position) => new(
        (position.x - terrainOrigin.x) / terrainSize.x,
        (position.z - terrainOrigin.z) / terrainSize.z);

    private Vector3 NormalizedToWorld(Vector2 point)
    {
        Vector3 position = terrainOrigin + new Vector3(point.x * terrainSize.x, 0f, point.y * terrainSize.z);
        position.y = SampleGroundHeight(position);
        return position;
    }

    private float SampleGroundHeight(Vector3 position) => terrainOrigin.y + terrain.SampleHeight(position);

    private void UpdateViewport()
    {
        if (gameplayCamera == null || !gameplayCamera.isActiveAndEnabled)
        {
            foreach (Image edge in outlineEdges)
                edge.enabled = false;
            hasViewportProjection = false;
            return;
        }

        Matrix4x4 view = gameplayCamera.worldToCameraMatrix;
        Matrix4x4 projection = gameplayCamera.projectionMatrix;
        if (hasViewportProjection && view == lastViewMatrix && projection == lastProjectionMatrix)
            return;

        lastViewMatrix = view;
        lastProjectionMatrix = projection;
        hasViewportProjection = true;

        Ray centerRay = gameplayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        float groundHeight = SampleGroundHeight(cameraController.transform.position);
        if (terrainCollider != null && terrainCollider.Raycast(centerRay, out RaycastHit hit, gameplayCamera.farClipPlane))
            groundHeight = hit.point.y;

        Rect mapRect = terrainImage.rectTransform.rect;
        for (int i = 0; i < outlineCorners.Length; i++)
        {
            Vector2 corner = ViewportCorners[i];
            Vector3 near = gameplayCamera.ViewportToWorldPoint(new Vector3(corner.x, corner.y, gameplayCamera.nearClipPlane));
            Vector3 far = gameplayCamera.ViewportToWorldPoint(new Vector3(corner.x, corner.y, gameplayCamera.farClipPlane));
            // Limit rays that reach the horizon to the camera's far plane.
            float t = near.y > far.y ? Mathf.InverseLerp(near.y, far.y, groundHeight) : 1f;
            Vector2 mapped = WorldToNormalized(Vector3.LerpUnclamped(near, far, t));
            outlineCorners[i] = mapRect.min + Vector2.Scale(mapped, mapRect.size);
        }

        for (int i = 0; i < outlineEdges.Length; i++)
        {
            Vector2 start = outlineCorners[i];
            Vector2 end = outlineCorners[(i + 1) % outlineCorners.Length];
            Vector2 direction = end - start;
            Image edge = outlineEdges[i];
            edge.enabled = direction.sqrMagnitude > 0f;
            edge.rectTransform.anchoredPosition = (start + end) * 0.5f;
            edge.rectTransform.sizeDelta = new Vector2(direction.magnitude, viewportLineWidth);
            edge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && navigationPointerId == null &&
            TryNavigate(eventData, requireInsideMap: true))
            navigationPointerId = eventData.pointerId;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            eventData.useDragThreshold = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && navigationPointerId == eventData.pointerId)
            TryNavigate(eventData, requireInsideMap: false);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && navigationPointerId == eventData.pointerId)
            navigationPointerId = null;
    }

    private bool TryNavigate(PointerEventData eventData, bool requireInsideMap)
    {
        if (terrain == null || terrainImage == null || cameraController == null || !cameraController.isActiveAndEnabled ||
            gameplayCamera == null || !gameplayCamera.isActiveAndEnabled)
            return false;

        UpdateMapLayout();
        RectTransform map = terrainImage.rectTransform;
        Rect rect = map.rect;
        if (rect.width <= 0f || rect.height <= 0f ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(map, eventData.position, eventData.pressEventCamera,
                out Vector2 localPoint) || (requireInsideMap && !rect.Contains(localPoint)))
            return false;

        Vector3 target = NormalizedToWorld(Rect.PointToNormalized(rect, localPoint));
        Ray centerRay = gameplayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        Vector3 rootPosition = cameraController.transform.position;
        Plane targetPlane = new(Vector3.up, target);
        if (targetPlane.Raycast(centerRay, out float distance))
            target -= centerRay.GetPoint(distance) - rootPosition;

        cameraController.SetGroundPosition(target);
        UpdateViewport();
        return true;
    }

    private void OnEnable()
    {
        if (frame != null && terrain != null && terrain.terrainData != null)
        {
            frame.gameObject.SetActive(true);
            timeUntilMarkerUpdate = 0f;
            hasViewportProjection = false;
        }
    }

    private void OnDisable()
    {
        navigationPointerId = null;
        if (frame != null)
            frame.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        ReleaseCapture();
        if (frame != null)
            Destroy(frame.gameObject);
    }

    private void ReleaseCapture()
    {
        if (captureCamera != null)
            Destroy(captureCamera.gameObject);
        if (terrainTexture != null)
        {
            if (terrainImage != null)
            {
                terrainImage.texture = null;
                terrainImage.enabled = false;
            }
            terrainTexture.Release();
            Destroy(terrainTexture);
        }

        captureCamera = null;
        terrainTexture = null;
        captureRequest = null;
        terrainCaptured = false;
    }

    private static T CreateViewObject<T>(string name, RectTransform parent) where T : UnityEngine.UI.Graphic
    {
        GameObject child = new(name, typeof(RectTransform), typeof(T));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        T graphic = child.GetComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static RectTransform CreateLayer(string name, RectTransform parent)
    {
        RectTransform layer = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        layer.SetParent(parent, false);
        StretchToParent(layer);
        return layer;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private sealed class MarkerPool
    {
        private readonly RectTransform layer;
        private readonly List<Image> markers = new();
        private int usedCount;
        private int previousCount;

        public MarkerPool(string name, RectTransform parent)
        {
            layer = CreateLayer(name, parent);
        }

        public void Add(Vector2 position, Vector2 size, Color color, float rotation = 0f)
        {
            if (usedCount == markers.Count)
            {
                Image created = CreateViewObject<Image>("Marker", layer);
                created.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
                markers.Add(created);
            }

            Image marker = markers[usedCount++];
            RectTransform rect = marker.rectTransform;
            rect.anchorMin = position;
            rect.anchorMax = position;
            rect.sizeDelta = size;
            marker.color = color;
            marker.enabled = true;
        }

        public void FinishUpdate()
        {
            for (int i = usedCount; i < previousCount; i++)
                markers[i].enabled = false;

            previousCount = usedCount;
            usedCount = 0;
        }
    }
}
