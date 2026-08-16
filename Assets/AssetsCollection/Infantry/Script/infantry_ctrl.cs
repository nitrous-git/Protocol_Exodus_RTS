using UnityEngine;
using System.Collections;

public class infantry_ctrl : MonoBehaviour {
	
	
	private Animator anim;
	private CharacterController controller;
	public float speed = 6.0f;
	public float runSpeed = 3.0f;
	public float turnSpeed = 60.0f;	
	private Vector3 moveDirection = Vector3.zero;
	private float w_sp = 0.0f;
	private float r_sp = 0.0f;

	public float duration = 1.0F;
	public ParticleSystem flame;
	public float gravity = 20.0f;
	public int type_of = 1; 
		// types:
		// 1 - gunman
		// 2 - missiler
		// 3 - sniper
		// 4 - flamer
	public Light lig;
	private bool lig_on = false;


	// Use this for initialization
	void Start () 
	{						
		anim = GetComponent<Animator>();
		controller = GetComponent<CharacterController> ();
		w_sp = speed; //read walk speed
		r_sp = runSpeed; //read run speed
		runSpeed = 1;

	}
	
	// Update is called once per frame
	void Update () 
	{		
		if (Input.GetKey ("up")) 
		{	
			if (Input.GetKey (KeyCode.LeftShift))
			{		
				anim.SetInteger ("moving", 2);//run
				runSpeed = r_sp;
				
			}
			else
			{
				anim.SetInteger ("moving", 1);//walk
				runSpeed = w_sp;
			}
		}
		else 
		{
			anim.SetInteger ("moving", 0);
		}

	
		if (Input.GetMouseButtonDown (0)) //attack
		{ 
			anim.SetInteger ("moving", 3);
			flame.Play();
			lig_on = true;
		}
//---------------------------------------------------------------- Light logic
	if (type_of == 4) 
		{
			if (lig_on) {
				lig.enabled = true;
				lig.intensity = Mathf.Lerp (lig.intensity, 6.0f, Time.deltaTime * 4);
			} else if (lig.intensity >= 0.1f) {
				lig.intensity = Mathf.Lerp (lig.intensity, 0.0f, Time.deltaTime * 2);
			}

			if ((lig.intensity >= 5.96f) && (lig.intensity >= 0.5f)) {
				lig_on = ! lig_on;
			}
			if (lig.intensity <= 0.1f)
				lig.enabled = false;
		}
//-----------------------------------------------------------------


		if (Input.GetKey ("u"))  // death1
		{ 
			anim.SetInteger ("moving", 13);
		}
		if (Input.GetKey ("i")) // death1
		{ 
			anim.SetInteger ("moving", 14);
		}


		if (controller.isGrounded) 
		{
			moveDirection=transform.forward * Input.GetAxis ("Vertical") * speed * runSpeed;
			float turn = Input.GetAxis("Horizontal");
			transform.Rotate(0, turn * turnSpeed * Time.deltaTime, 0);						
		}
		moveDirection.y -= gravity * Time.deltaTime;
		controller.Move (moveDirection * Time.deltaTime);
		}

	}






