using UnityEngine;
using System.Collections;

public class DevilMechDemoGUI : MonoBehaviour {

	public bool ShowGUI = true;

	public GUIStyle demoGUIStyle;
	
	public string packageNameLabel = "Rabid Assault Mech ";
	public string versionLabel = "\n" + "v1.0";
	public string packageCreatorLabel = "Created by, Daniel Kole Productions";

	private string instructions = "";

	// Use this for initialization
	void Start () {
		instructions = packageNameLabel + "\n" + versionLabel + "\n" + packageCreatorLabel;
		instructions += "\n";
		instructions += "\n" + "Demo Instructions: ";
		instructions += "\n" + "\n" + "WASD - Move Mech Forward, Back, Turn Right, and Turn Left";
		instructions += "\n" + "\n" + "QE - Strafe Mech Left and Right";
		instructions += "\n" + "F - Attack";
		instructions += "\n" + "T - Right Sword Hack";
		instructions += "\n" + "Y - Left Sword Swing";
		instructions += "\n" + "BACKSPACE - Backward Death";
		instructions += "\n" + "P - Forward Death";
		instructions += "\n" + "H - Take Hit From Behind";
		instructions += "\n" + "J - Take Hit From Ahead";
		instructions += "\n" + "SPACE - Jump";
		instructions += "\n";
		instructions += "\n" + "TAB - Change Mech Skin";
	}

	void OnGUI() {
		if (ShowGUI) {
			GUI.Label(new Rect(10, 10, 250, 50), instructions, demoGUIStyle);
		}
	}

	// Update is called once per frame
	void Update () {
	
	}
}
