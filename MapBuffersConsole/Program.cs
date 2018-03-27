using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.iOS;
using UnityEngine.Networking;
using System.Linq;
using System;

namespace MapBuffersConsole
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }
    }

    [System.Serializable]
    public class UploadBase
    {
        public string action;
        public string map_version = "Unity-0.9.1";
        public string host = "https://upload.mapcha.in/v1/";
        public string session;
    }

    [System.Serializable]
    public class SessionUpload : UploadBase
    {
        public string client;
        public string map_key;
        public float heading_accuracy;
        public float heading;
        public float lat;
        public float lon;
        public float gps_accuracy;
        public double gps_timestamp;
        public float altitude;
        public string os_version;
        public string device_type;
        public string device_identifier;

        public SessionUpload(string client_id, string session_id)
        {
            client = client_id;
            session = session_id;
            action = "session_start";
            device_type = SystemInfo.deviceModel;
            os_version = SystemInfo.operatingSystem;
        }
    }

    [System.Serializable]
    public class DataUpload : UploadBase
    {
        public List<Vector3> points;
        public float heading_accuracy;
        public float heading;
        public float lat;
        public float lon;
        public float gps_accuracy;
        public double gps_timestamp;
        public float altitude;
        public List<Vector3> plane_positions;
        public List<string> plane_rotations;
        public List<Vector3> plane_sizes;
        public List<string> plane_ids;
        public List<int> plane_orientations;
        public List<Vector3> plane_normals;
        public string id;
        public int seq;

        public DataUpload(string session_id)
        {
            session = session_id;
            action = "data_upload";
        }
    }

    [System.Serializable]
    public class IndividualPlaneUpdate
    {
        public string planeId;
        public Vector3 position;
        public Vector4 rotation;
        public Vector3 locPosition;
        public Vector4 locRotation;
        public int alignment;
        public List<Vector3> boundaryVertices;
        public List<Vector2> textureCoordinates;
        public List<int> triangleIndices;
        public List<Vector3> vertices;
    }

    [System.Serializable]
    public class PlaneUpdate : UploadBase
    {
        public List<IndividualPlaneUpdate> planes;

        public PlaneUpdate(string session_id)
        {
            session = session_id;
            action = "plane_upload";
            planes = new List<IndividualPlaneUpdate>();
        }
    }

    public class MAPManager : MonoBehaviour
    {
        private string client_id;
        private string session_id;
        public string mapAppKey;
        public string mapWalletAddress;
        public UnityARAnchorManager unityARAnchorManager;
        public bool capturePointCloud;
        private List<Vector3> stored_points;
        private DataUpload point_upload;

        IEnumerator Start()
        {
            if (PlayerPrefs.HasKey("mapplatform.client_id"))
            {
                client_id = PlayerPrefs.GetString("mapplatform.client_id");
            }
            else
            {
                client_id = System.Guid.NewGuid().ToString();
                PlayerPrefs.SetString("mapplatform.client_id", client_id);
                PlayerPrefs.Save();
            }

            session_id = System.Guid.NewGuid().ToString();
            unityARAnchorManager = new UnityARAnchorManager();
            stored_points = new List<Vector3>();
            if (!Input.location.isEnabledByUser)
            {
                print("MAP requires GPS location to be enabeld for the application.");
                yield break;
            }
            // Start service before querying location
            Input.location.Start();
            Input.compass.enabled = true;

            // Wait until service initializes
            int maxWait = 20;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                yield return new WaitForSeconds(1);
                maxWait--;
            }
            // Service didn't initialize in 20 seconds
            if (maxWait < 1)
            {
                print("Timed out");
            }
            // Connection has failed
            if (Input.location.status == LocationServiceStatus.Failed)
            {
                print("Unable to determine device location");
            }

            // Start session
            SessionUpload session_start_payload = new SessionUpload(client_id, session_id);
            session_start_payload.map_key = mapAppKey;
            session_start_payload.heading_accuracy = Input.compass.headingAccuracy;
            session_start_payload.lat = Input.location.lastData.latitude;
            session_start_payload.lon = Input.location.lastData.longitude;
            session_start_payload.altitude = Input.location.lastData.altitude;
            session_start_payload.gps_accuracy = Input.location.lastData.horizontalAccuracy;
            session_start_payload.gps_timestamp = Input.location.lastData.timestamp;

            StartCoroutine(UploadPayload(session_start_payload.host, JsonUtility.ToJson(session_start_payload)));

            point_upload = new DataUpload(session_id);
            point_upload.points = new List<Vector3>();
            InvokeRepeating("GeneratePlaneGeometryObjects", 15.0f, 15.0f);
            if (capturePointCloud)
            {
                UnityARSessionNativeInterface.ARFrameUpdatedEvent += CollectPointsFromBuffer;
                InvokeRepeating("PublishPointCloud", 7.0f, 4.0f);
            }
        }



        IEnumerator UploadPayload(string host, string payload)
        {
            WWWForm form = new WWWForm();
            //form.AddField("points", JsonHelper.Json(m_PointCloudData));
            form.AddField("points", payload);
            using (UnityWebRequest www = UnityWebRequest.Post(host, form))
            {
                yield return www.Send();

                if (www.isNetworkError || www.isHttpError)
                {
                   
                    StartCoroutine(UploadPayload(host, payload));
                    Debug.Log(www.error);
                }
                else
                {
                    Debug.Log("MAP: Upload complete!");
                }
            }

        }

        public void CollectPointsFromBuffer(UnityARCamera camera)
        {
            if (camera.pointCloudData != null && camera.pointCloudData.Length > 0)
            {
                point_upload.points.AddRange(new List<Vector3>(camera.pointCloudData));
            }
        }

        public void PublishPointCloud()
        {
            StartCoroutine(UploadPayload(point_upload.host, JsonUtility.ToJson(point_upload)));
            point_upload.points = new List<Vector3>();
        }

        public void GeneratePlaneGeometryObjects()
        {
            PlaneUpdate payload = new PlaneUpdate(session_id);
            if (unityARAnchorManager == null) unityARAnchorManager = new UnityARAnchorManager();
            List<ARPlaneAnchorGameObject> arpags = unityARAnchorManager.GetCurrentPlaneAnchors();
            if (arpags.Count == 0)
            {
                Debug.Log("No planes detected at this time..");
                return;
            }
            foreach (ARPlaneAnchorGameObject plane_anchor in arpags)
            {
                IndividualPlaneUpdate plane = new IndividualPlaneUpdate();
                if (plane_anchor.planeAnchor.planeGeometry == null)
                {
                    Debug.Log("No plane geometry for plane " + plane_anchor.planeAnchor.identifier);
                    continue;
                }
                plane.planeId = plane_anchor.planeAnchor.identifier;
                plane.position = plane_anchor.planeAnchor.center;
                plane.locPosition = plane_anchor.gameObject.transform.localPosition;
                Quaternion plane_rot = plane_anchor.gameObject.transform.rotation;
                plane.rotation = new Vector4(plane_rot.x, plane_rot.y, plane_rot.z, plane_rot.w);
                Quaternion plane_loc_rot = plane_anchor.gameObject.transform.localRotation;
                plane.locRotation = new Vector4(plane_loc_rot.x, plane_loc_rot.y, plane_loc_rot.z, plane_loc_rot.w);
                plane.alignment = (int)plane_anchor.planeAnchor.alignment;
                plane.boundaryVertices = plane_anchor.planeAnchor.planeGeometry.boundaryVertices.ToList();
                plane.textureCoordinates = plane_anchor.planeAnchor.planeGeometry.textureCoordinates.ToList();
                plane.triangleIndices = plane_anchor.planeAnchor.planeGeometry.triangleIndices.ToList();
                plane.vertices = plane_anchor.planeAnchor.planeGeometry.vertices.ToList();
                payload.planes.Add(plane);
            }
            StartCoroutine(UploadPayload(payload.host, JsonUtility.ToJson(payload)));
        }

        void OnDestroy()
        {
            unityARAnchorManager.Destroy();
        }
    }
}
