using UnityEngine;
using System.Collections;

public class DevilCameraController : MonoBehaviour {

	private Transform myTransform;
	public Transform CameraTransform;

	public bool FollowBehindTarget = false;
	public Transform TargetObjectTransform;
	public float LerpSpeed = 3;
	public float RotationLerpSpeed = 2;

	public bool RotateCamera = true;
	public float RotationSpeed = 2;

	// Use this for initialization
	void Start () {
		myTransform = gameObject.transform;
	}
	
	// Update is called once per frame
	void Update () {
		if (TargetObjectTransform != null) {
			if (CameraTransform != null) {
				CameraTransform.LookAt(TargetObjectTransform.position);
			}
			myTransform.position = Vector3.Lerp(myTransform.position, TargetObjectTransform.position, LerpSpeed * Time.deltaTime);
			if (FollowBehindTarget) {
				myTransform.rotation = Quaternion.Lerp(myTransform.rotation, TargetObjectTransform.rotation, RotationLerpSpeed * Time.deltaTime);
			}
			if (RotateCamera) {
				myTransform.RotateAround(myTransform.position, myTransform.up, RotationSpeed * Time.deltaTime);
			}
		}
	}
}
