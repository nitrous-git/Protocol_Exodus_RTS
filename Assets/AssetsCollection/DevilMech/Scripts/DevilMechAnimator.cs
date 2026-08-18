using UnityEngine;
using System.Collections;

public enum DevilMechAnimations {
	Idle, // 0
	Walk, // 1
	WalkBackward, // 2
	StrafeRight, // 3
	StrafeLeft, // 4
	TurnLeft, // 5
	TurnRight, // 6
	HitFromFront, // 7
	HitFromBack, // 8
	DieBackward, // 9
	DieForward, // 10
	Jump, // 11
	Attack, // 12
	RightSwordHack, // 13
	LeftSwordSwing // 14
}

public class DevilMechAnimator : MonoBehaviour {

	public bool IsAlive = true;
	public bool StayDead = false;

	public bool UseNoMovement = false;

	public DevilMechAnimations CurrentAnimation = DevilMechAnimations.Idle;
	public DevilMechAnimations DesiredAnimation = DevilMechAnimations.Idle;
	public Animator MechAnimator;

	private Transform mechTransform;
	private Rigidbody mechRigidbody;

	public float ForwardMoveSpeed = 3f;
	public float StrafeMoveSpeed = 2f;
	public float RotationSpeed = 2500f;

	private bool hit = false;
	private float hitTimerFreq = 0.1f;
	private float hitTimer = 0;

	// Use this for initialization
	void Start () {
		mechTransform = gameObject.transform;
		mechRigidbody = gameObject.GetComponent<Rigidbody>();
	}
	
	// Update is called once per frame
	void Update () {
		if (MechAnimator != null) {
			bool usingKeys = false;

			if (IsAlive) {
				if (Input.GetKey(KeyCode.W)) {
					DesiredAnimation = DevilMechAnimations.Walk;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.S)) {
					DesiredAnimation = DevilMechAnimations.WalkBackward;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.A)) {
					DesiredAnimation = DevilMechAnimations.TurnLeft;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.D)) {
					DesiredAnimation = DevilMechAnimations.TurnRight;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.Q)) {
					DesiredAnimation = DevilMechAnimations.StrafeLeft;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.E)) {
					DesiredAnimation = DevilMechAnimations.StrafeRight;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.H)) {
					DesiredAnimation = DevilMechAnimations.HitFromFront;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.J)) {
					DesiredAnimation = DevilMechAnimations.HitFromBack;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.Backspace)) {
					DesiredAnimation = DevilMechAnimations.DieBackward;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.P)) {
					DesiredAnimation = DevilMechAnimations.DieForward;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.Space)) {
					DesiredAnimation = DevilMechAnimations.Jump;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.F)) {
					DesiredAnimation = DevilMechAnimations.Attack;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.T)) {
					DesiredAnimation = DevilMechAnimations.RightSwordHack;
					usingKeys = true;
				}
				if (Input.GetKey(KeyCode.Y)) {
					DesiredAnimation = DevilMechAnimations.LeftSwordSwing;
					usingKeys = true;
				}
			
				if (hit) {
					if (hitTimer < hitTimerFreq) {
						hitTimer += Time.deltaTime;
					}
					else {
						hitTimer = 0;
						hit = false;
					}
				}
				else {
					if (!usingKeys) {
						DesiredAnimation = DevilMechAnimations.Idle;
					}
				}
			}

			// Resurrect
			if (Input.GetKey(KeyCode.R)) {
				DesiredAnimation = DevilMechAnimations.Idle;
				IsAlive = true;
				usingKeys = true;
			}

			MechAnimator.SetInteger("CurrentAnimation", (int)DesiredAnimation);

			if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle"))
			{
				CurrentAnimation = DevilMechAnimations.Idle;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("Walk"))
			{
				CurrentAnimation = DevilMechAnimations.Walk;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("WalkBackward"))
			{
				CurrentAnimation = DevilMechAnimations.WalkBackward;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("StrafeLeft"))
			{
				CurrentAnimation = DevilMechAnimations.StrafeLeft;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("StrafeRight"))
			{
				CurrentAnimation = DevilMechAnimations.StrafeRight;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("TurnLeft"))
			{
				CurrentAnimation = DevilMechAnimations.TurnLeft;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("TurnRight"))
			{
				CurrentAnimation = DevilMechAnimations.TurnRight;
			}			
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("HitFromFront"))
			{
				CurrentAnimation = DevilMechAnimations.HitFromFront;
				hit = true;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("HitFromBack"))
			{
				CurrentAnimation = DevilMechAnimations.HitFromBack;
				hit = true;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("DieBackward"))
			{
				CurrentAnimation = DevilMechAnimations.DieBackward;
				if (DesiredAnimation == DevilMechAnimations.DieBackward)
					IsAlive = false;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("DieForward"))
			{
				CurrentAnimation = DevilMechAnimations.DieForward;
				if (DesiredAnimation == DevilMechAnimations.DieForward)
					IsAlive = false;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("Jump"))
			{
				CurrentAnimation = DevilMechAnimations.Jump;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("Attack"))
			{
				CurrentAnimation = DevilMechAnimations.Attack;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("RightSwordHack"))
			{
				CurrentAnimation = DevilMechAnimations.RightSwordHack;
			}
			else if(MechAnimator.GetCurrentAnimatorStateInfo(0).IsName("LeftSwordSwing"))
			{
				CurrentAnimation = DevilMechAnimations.LeftSwordSwing;
			}

			MechAnimator.SetBool("IsAlive", IsAlive);
			MechAnimator.SetBool("Hit", hit);
		}
	}
	
	void FixedUpdate() {
		if (!UseNoMovement) {
			if (mechRigidbody != null) {
				if (CurrentAnimation == DevilMechAnimations.Walk) {
					Vector3 velocityForward = mechTransform.forward * ForwardMoveSpeed;
					mechRigidbody.linearVelocity = velocityForward;
				}
				else if (CurrentAnimation == DevilMechAnimations.WalkBackward) {
					Vector3 velocityForward = mechTransform.forward * -ForwardMoveSpeed;
					mechRigidbody.linearVelocity = velocityForward;
				}
				else if (CurrentAnimation == DevilMechAnimations.StrafeLeft) {
					Vector3 velocityForward = mechTransform.right * -ForwardMoveSpeed;
					mechRigidbody.linearVelocity = velocityForward;
				}
				else if (CurrentAnimation == DevilMechAnimations.StrafeRight) {
					Vector3 velocityForward = mechTransform.right * ForwardMoveSpeed;
					mechRigidbody.linearVelocity = velocityForward;
				}
				else if (CurrentAnimation == DevilMechAnimations.TurnLeft) {
					Vector3 rotationVelocity = new Vector3(0, -(RotationSpeed * Time.deltaTime), 0);
					Quaternion deltaRotation = Quaternion.Euler(rotationVelocity * Time.deltaTime);
					mechRigidbody.MoveRotation(mechRigidbody.rotation * deltaRotation);
				}
				else if (CurrentAnimation == DevilMechAnimations.TurnRight) {
					Vector3 rotationVelocity = new Vector3(0, RotationSpeed * Time.deltaTime, 0);
					Quaternion deltaRotation = Quaternion.Euler(rotationVelocity * Time.deltaTime);
					mechRigidbody.MoveRotation(mechRigidbody.rotation * deltaRotation);
				}
			}
		}
	}
}
