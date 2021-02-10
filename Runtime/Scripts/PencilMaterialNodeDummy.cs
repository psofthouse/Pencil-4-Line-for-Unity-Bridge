using System.Collections.Generic;
using UnityEngine;
using Pencil_4;

namespace Pencil4_Bridge
{

	[JsonAlias("PencilMaterial")]
	public class PencilMaterialNodeDummy
	{
		[JsonDataMember] public Color BasicMaterial = Color.gray;
		[JsonDataMember] public GameObject AdvancedMaterial;

		[JsonDataMember] public int BlendMode = 0;
		[JsonDataMember] public float BlendAmount = 1.0f;
		[JsonDataMember] public Color HighlightColor = Color.white;
		[JsonDataMember] public bool ColorMapOn = true;
		[JsonDataMember] public GameObject ColorMap = null;
		[JsonDataMember] public float MapOpacity = 1.0f;
		[JsonDataMember] public float SpecularLevel;
		[JsonDataMember] public float Glossiness = 0.1f;
		[JsonDataMember] public float Anisotropic = 0.0f;
		[JsonDataMember] public int Orientation = 0;
		[JsonDataMember] public float Sharpness = 0.0f;
		[JsonDataMember] public float Squash = 0.0f;
		[JsonDataMember] public float DiffractionEffect = 0.0f;
		[JsonDataMember] public float Range = 0.0f;
		[JsonDataMember] public bool BumpOn = false;
		[JsonDataMember] public float BumpAmount = 0.0f;
		[JsonDataMember] public bool BumpDisableHighlight = false;
		[JsonDataMember] public bool ReflectionOn = false;
		[JsonDataMember] public float ReflectionAmount = 1.0f;
		[JsonDataMember] public Color ReflectionColor = Color.white;

		[JsonDataMember] public Color BasicMaterialTransparency = Color.clear;

		[JsonDataMember] public GameObject LineFunctions = null;
		[JsonDataMember] public GradationDummy Gradation = new GradationDummy();
		[JsonDataMember] public UserDefDummy UserDef = new UserDefDummy();
	}

//  AdvancedMaterialの雛形。今の所使っていないのでコメントアウト
//	public class AdvancedMaterialDummy
//	{
//		[JsonDataMember] float GradOffsetAmount = 1.0f;
//		[JsonDataMember] bool GradOffsetEnable = false;
//		[JsonDataMember] GameObject GradOffsetMap = null;
//		[JsonDataMember] float GradOffsetMapOffset = -0.5f;
//		[JsonDataMember] public Object HiDesignAmount = null;
//		[JsonDataMember] public Object HiDesignAngle = null;
//		[JsonDataMember] public Object HiDesignEnable  = null;
//		[JsonDataMember] public Object LightColBlend  = null;
//		[JsonDataMember] public Object LightColEnable  = null;
//		[JsonDataMember] public Object LightColRelpaceColor  = null;
//		[JsonDataMember] public Object LightColRelpaceEnable  = null;
//		[JsonDataMember] public Object LightColReplaceAmount  = null;
//		[JsonDataMember] public Object LightcolAmount  = null;
//		[JsonDataMember] public Object LightcolZoneIDs = null;
//		[JsonDataMember] public Object PolygonCntl  = null;
//		[JsonDataMember] public Object SblendAmount  = null;
//		[JsonDataMember] public Object SblendColor  = null;
//		[JsonDataMember] public Object SblendEnable  = null;
//		[JsonDataMember] public Object SblendMap  = null;
//		[JsonDataMember] public Object SblendMapEnable  = null;
//		[JsonDataMember] public Object SblendMapOpacity  = null;
//		[JsonDataMember] public Object SblendMode  = null;
//		[JsonDataMember] public Object SpecificLightsEnable = null; 
//		[JsonDataMember] public Object SpecificLightsStrength  = null;
//		[JsonDataMember] public Object SpecificTranspEnable  = null;
//		[JsonDataMember] public Object SpecificTranspMapAmount  = null;
//		[JsonDataMember] public Object SpecificTranspMapEnable  = null;
//		[JsonDataMember] public Object SpecificTranspMaterials  = null;
//		[JsonDataMember] public Object SpecificTranspMaterialsValue  = null;
//		[JsonDataMember] public Object ZoneAntiEnable  = null;
//		[JsonDataMember] public Object ZoneAntiIntensity = null;
//	}

	public class GradationDummy
	{
		[JsonDataMember] public List<UniversalGradationDummy> UniversalGradation = new List<UniversalGradationDummy>
		{
			new UniversalGradationDummy
			{
				Color = Color.black,
				Position = 0.0f
			},
			new UniversalGradationDummy
			{
				Color = new Color(0.333f, 0.333f, 0.333f, 1.0f),
				Position = 0.25f
			},
			new UniversalGradationDummy
			{
				Color = new Color(0.667f, 0.667f, 0.667f, 1.0f),
				Position = 0.5f
			},
			new UniversalGradationDummy
			{
				Color = Color.white,
				Position = 0.75f
			}
		};
	}

	public class UniversalGradationDummy
	{
		[JsonDataMember] public float BlendAmount = 1.0f;
		[JsonDataMember] public int BlendMode = 0;
		[JsonDataMember] public Color Color;
		[JsonDataMember] public GameObject ColorMap = null;
		[JsonDataMember] public bool ColorMapOn = true;
		[JsonDataMember] public bool Enable = true;
		[JsonDataMember] public int Interpolation = 0;
		[JsonDataMember] public float MapOpacity = 1.0f;
		[JsonDataMember] public float Position;
	}

	public class UserDefDummy
	{
		[JsonDataMember] public string Comment = "";
		[JsonDataMember] public Color Label = new Color(0.267f, 0.267f, 0.267f, 1.0f);
	}
}

