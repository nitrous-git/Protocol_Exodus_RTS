using UnityEngine;
using System.Collections;

public class FollowTargetPos : MonoBehaviour {

	public Transform TargetObject;

	private Transform myTransform;

	public float LerpSpeed = 3f;
	public float YOffset = 1.5f;

	// Use this for initialization
	void Start () {
		myTransform = gameObject.transform;
	}
	
	// Update is called once per frame
	void Update () {
		if (TargetObject) {
			Vector3 targetPositon = TargetObject.position;
			targetPositon.y += YOffset;
			myTransform.position = Vector3.Lerp(myTransform.position, targetPositon, LerpSpeed * Time.deltaTime);
		}
	}
}
