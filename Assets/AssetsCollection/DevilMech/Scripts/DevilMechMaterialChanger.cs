using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum DevilMechMaterialTypes {
	SciFiOriginal,
	DesertMaterial,
	SnowCammoMaterial,
	GreenCammoMaterial,
	RedBlackMaterial
}

public class DevilMechMaterialChanger : MonoBehaviour {

	public Material DevilMechBodyMaterial;
	public Material DesertBodyMaterial;
	public Material SnowCammoBodyMaterial;
	public Material GreenCammoBodyMaterial;
	public Material RedBlackBodyMaterial;

	private List<Renderer> BodyRenderers;

	public DevilMechMaterialTypes ActiveMaterialType = DevilMechMaterialTypes.SciFiOriginal;
	public DevilMechMaterialTypes DesiredMaterialType = DevilMechMaterialTypes.SciFiOriginal;

	// Use this for initialization
	void Start () {
		// Find all Leg and Body Renderers
		Renderer[] allRenderers = gameObject.GetComponentsInChildren<Renderer>();
//		Debug.Log(allRenderers.Length.ToString() + " Renderers Found Total.");

		// Initialize Lists
		BodyRenderers = new List<Renderer>();

		// Seperate Renderers
		if (allRenderers.Length > 0) {
			for (int i = 0; i < allRenderers.Length; i++) {
				if (allRenderers[i].sharedMaterial.name == DevilMechBodyMaterial.name) {
					BodyRenderers.Add(allRenderers[i]);
				}
			}
		}
		allRenderers = null;
	}
	
	// Update is called once per frame
	void Update () {
		
		if (Input.GetKeyUp(KeyCode.Tab)) {
			if (DesiredMaterialType == DevilMechMaterialTypes.SciFiOriginal)
				DesiredMaterialType = DevilMechMaterialTypes.DesertMaterial;
			else if (DesiredMaterialType == DevilMechMaterialTypes.DesertMaterial)
				DesiredMaterialType = DevilMechMaterialTypes.SnowCammoMaterial;
			else if (DesiredMaterialType == DevilMechMaterialTypes.SnowCammoMaterial)
				DesiredMaterialType = DevilMechMaterialTypes.GreenCammoMaterial;
			else if (DesiredMaterialType == DevilMechMaterialTypes.GreenCammoMaterial)
				DesiredMaterialType = DevilMechMaterialTypes.RedBlackMaterial;
			else if (DesiredMaterialType == DevilMechMaterialTypes.RedBlackMaterial)
				DesiredMaterialType = DevilMechMaterialTypes.SciFiOriginal;
		}

		if (DesiredMaterialType != ActiveMaterialType) {
			UpdateMaterials();
		}
	}

	private void UpdateMaterials() {		
		ActiveMaterialType = DesiredMaterialType;

		if (ActiveMaterialType == DevilMechMaterialTypes.SciFiOriginal) {
			UpdateBodyMaterials(DevilMechBodyMaterial);
		}
		else if (ActiveMaterialType == DevilMechMaterialTypes.DesertMaterial) {
			UpdateBodyMaterials(DesertBodyMaterial);
		}
		else if (ActiveMaterialType == DevilMechMaterialTypes.SnowCammoMaterial) {
			UpdateBodyMaterials(SnowCammoBodyMaterial);
		}
		else if (ActiveMaterialType == DevilMechMaterialTypes.GreenCammoMaterial) {
			UpdateBodyMaterials(GreenCammoBodyMaterial);
		}
		else if (ActiveMaterialType == DevilMechMaterialTypes.RedBlackMaterial) {
			UpdateBodyMaterials(RedBlackBodyMaterial);
		}
	}

	private void UpdateBodyMaterials(Material materialToUse) {
		if (BodyRenderers.Count > 0) {
			for (int i = 0; i < BodyRenderers.Count; i++) {
				BodyRenderers[i].material = materialToUse;
			}
		}
	}

}