using Sandbox;
using ValvePak;
using ValveKeyValue;
using System;

internal class HL2Material : ResourceLoader<HL2Mount>
{
	private readonly VpkArchive _package;
	private readonly VpkEntry _entry;
	private readonly string _filePath;

	public HL2Material( VpkArchive package, VpkEntry entry )
	{
		_package = package;
		_entry = entry;
	}

	public HL2Material( string filePath )
	{
		_filePath = filePath;
	}

	protected override object Load()
	{
		byte[] data;
		if ( _package != null )
		{
			_package.ReadEntry( _entry, out data );
		}
		else
		{
			data = File.ReadAllBytes( _filePath );
		}
		return VmtLoader.Load( data, Path );
	}
}

internal static class VmtLoader
{
	public static Material Load( byte[] data, string path )
	{
		var vmt = KeyValues.Parse( data );
		if ( vmt == null )
		{
			return null;
		}

		// Merge conditional blocks (>=dx90, etc.)
		vmt.MergeConditionals();

		var shaderName = vmt.Name?.ToLowerInvariant();
		var material = Material.Create( path, shaderName );

		SetShaderFeatures( material, shaderName, vmt );

		foreach ( var prop in vmt.Children )
		{
			ApplyMaterialParameter( material, prop.Name, prop.Value );
		}

		// Special case: phong exponent from texture
		if ( shaderName == "vertexlitgeneric" && vmt.GetBool( "$phong" ) )
		{
			if ( vmt["$phongexponenttexture"] != null && vmt["$phongexponent"] == null )
			{
				material.Set( "g_flPhongExponent", -1.0f );
			}
		}

		return material;
	}

	private static void SetShaderFeatures( Material material, string shaderName, KeyValues vmt )
	{
		bool hasSelfIllum = vmt.GetBool( "$selfillum" );

		if ( vmt.GetBool( "$translucent" ) )
			material.SetFeature( "F_TRANSLUCENT", 1 );

		if ( vmt.GetBool( "$alphatest" ) && !hasSelfIllum && !vmt.GetBool( "$basealphaenvmapmask" ) )
			material.SetFeature( "F_ALPHA_TEST", 1 );

		if ( vmt.GetBool( "$additive" ) )
		{
			material.SetFeature( "F_TRANSLUCENT", 1 );
			material.SetFeature( "F_ADDITIVE_BLEND", 1 );
		}

		if ( vmt.GetBool( "$nocull" ) )
			material.SetFeature( "F_RENDER_BACKFACES", 1 );

		if ( TextureExists( vmt.GetString( "$bumpmap" ) ?? vmt.GetString( "$normalmap" ) ) )
			material.SetFeature( "F_BUMPMAP", 1 );

		if ( hasSelfIllum )
			material.SetFeature( "F_SELFILLUM", 1 );

		if ( TextureExists( vmt.GetString( "$detail" ) ) )
			material.SetFeature( "F_DETAIL", 1 );

		var envmap = vmt.GetString( "$envmap" );
		// Asphaltian: Temporarily discard "env_cubemap" since it looks off currently
		if ( !string.IsNullOrEmpty( envmap ) && envmap != "0" && !envmap.Equals( "env_cubemap", StringComparison.OrdinalIgnoreCase ) )
			material.SetFeature( "F_ENVMAP", 1 );

		switch ( shaderName )
		{
			case "vertexlitgeneric":
				if ( vmt.GetBool( "$phong" ) )
					material.SetFeature( "F_PHONG", 1 );
				if ( vmt.GetBool( "$rimlight" ) )
					material.SetFeature( "F_RIMLIGHT", 1 );
				if ( vmt.GetBool( "$halflambert" ) )
					material.SetFeature( "F_HALFLAMBERT", 1 );
				break;

			case "lightmappedgeneric":
				if ( TextureExists( vmt.GetString( "$basetexture2" ) ) )
					material.SetFeature( "F_BLEND", 1 );
				if ( vmt.GetBool( "$seamless_base" ) || vmt["$seamless_scale"] != null )
					material.SetFeature( "F_SEAMLESS", 1 );
				break;

			case "unlitgeneric":
				if ( vmt.GetBool( "$vertexcolor" ) )
					material.SetFeature( "F_VERTEX_COLOR", 1 );
				if ( vmt.GetBool( "$vertexalpha" ) )
					material.SetFeature( "F_VERTEX_ALPHA", 1 );
				break;
		}
	}

	private static bool TextureExists( string textureName )
	{
		if ( string.IsNullOrWhiteSpace( textureName ) )
			return false;

		textureName = textureName.Replace( '\\', '/' );
		var path = $"mount://hl2/materials/{textureName}.vtex";
		var texture = Texture.Load( path, false );
		return texture.IsValid() && !texture.IsError;
	}

	private static void ApplyMaterialParameter( Material material, string key, string value )
	{
		if ( string.IsNullOrEmpty( key ) || string.IsNullOrEmpty( value ) )
			return;

		key = key.ToLowerInvariant();

		switch ( key )
		{
			// Base Textures
			case "$basetexture": LoadAndSetTexture( material, value, "g_tColor" ); break;
			case "$basetexture2": LoadAndSetTexture( material, value, "g_tColor2" ); break;
			case "$bumpmap":
			case "$normalmap": LoadAndSetTexture( material, value, "g_tNormal" ); break;
			case "$bumpmap2": LoadAndSetTexture( material, value, "g_tNormal2" ); break;
			case "$detail": LoadAndSetTexture( material, value, "g_tDetail" ); break;
			case "$envmapmask": LoadAndSetTexture( material, value, "g_tEnvMapMask" ); break;
			case "$phongexponenttexture": LoadAndSetTexture( material, value, "g_tPhongExponent" ); break;
			case "$lightwarptexture":
				LoadAndSetTexture( material, value, "g_tLightWarp" );
				material.Set( "g_flLightWarpEnabled", 1.0f );
				break;
			case "$phongwarptexture":
				LoadAndSetTexture( material, value, "g_tPhongWarp" );
				material.Set( "g_flPhongWarpEnabled", 1.0f );
				break;
			case "$selfillummask":
				LoadAndSetTexture( material, value, "g_tSelfIllumMask" );
				material.Set( "g_flSelfIllumMaskControl", 1.0f );
				break;
			case "$iris": LoadAndSetTexture( material, value, "g_tIris" ); break;

			// Environment Map (special handling)
			case "$envmap":
				if ( value != "0" && !value.Equals( "env_cubemap", StringComparison.OrdinalIgnoreCase ) )
				{
					LoadAndSetTexture( material, value, "g_tEnvMap" );
					material.Set( "g_flUseExplicitEnvMap", 1.0f );
				}
				break;

			// Color and Alpha
			case "$color":
			case "$color2": SetVector( material, "g_vColorTint", value ); break;
			case "$alpha": SetFloat( material, "g_flAlpha", value ); break;
			case "$alphatestreference": SetFloat( material, "g_flAlphaTestReference", value ); break;

			// Detail Texture
			case "$detailscale": SetFloat( material, "g_flDetailScale", value ); break;
			case "$detailblendfactor": SetFloat( material, "g_flDetailBlendFactor", value ); break;
			case "$detailblendmode": SetInt( material, "g_nDetailBlendMode", value ); break;

			// Phong Specular
			case "$phongexponent": SetFloat( material, "g_flPhongExponent", value ); break;
			case "$phongboost": SetFloat( material, "g_flPhongBoost", value ); break;
			case "$phongtint": SetVector( material, "g_vPhongTint", value ); break;
			case "$phongfresnelranges":
				var ranges = ParseVector( value );
				if ( ranges.HasValue )
				{
					var r = ranges.Value;
					var encoded = new Vector3( (r.y - r.x) * 2.0f, r.y, (r.z - r.y) * 2.0f );
					material.Set( "g_vPhongFresnelRanges", encoded );
				}
				break;
			case "$phongalbedotint": SetFloat( material, "g_flPhongAlbedoTint", value ); break;
			case "$basemapalphaphongmask": SetBool( material, "g_nBaseMapAlphaPhongMask", value ); break;

			// Self Illumination
			case "$selfillumtint": SetVector( material, "g_vSelfIllumTint", value ); break;
			case "$selfillumfresnelminmaxexp":
				var fresnelVec = ParseVector( value );
				if ( fresnelVec.HasValue )
				{
					var fv = fresnelVec.Value;
					float flMin = fv.x;
					float flMax = fv.y;
					float flExp = fv.z;
					float flBias = (flMax != 0.0f) ? (flMin / flMax) : 0.0f;
					float flScale = 1.0f - flBias;
					material.Set( "g_vSelfIllumFresnelParams", new Vector4( flScale, flBias, flExp, flMax ) );
				}
				break;

			// Rim Lighting
			case "$rimlightexponent": SetFloat( material, "g_flRimLightExponent", value ); break;
			case "$rimlightboost": SetFloat( material, "g_flRimLightBoost", value ); break;

			// Environment Map Parameters
			case "$envmaptint": SetVector( material, "g_vEnvMapTint", value ); break;
			case "$envmapcontrast": SetFloat( material, "g_flEnvMapContrast", value ); break;
			case "$envmapsaturation": SetFloat( material, "g_flEnvMapSaturation", value ); break;
			case "$envmapfresnel": SetFloat( material, "g_flEnvMapFresnel", value ); break;
			case "$basealphaenvmapmask": SetBool( material, "g_nBaseAlphaEnvMapMask", value ); break;
			case "$normalmapalphaenvmapmask": SetBool( material, "g_nNormalMapAlphaEnvMapMask", value ); break;
			case "$selfillum_envmapmask_alpha": SetFloat( material, "g_flSelfIllumEnvMapMaskAlpha", value ); break;
			case "$fresnelreflection": SetFloat( material, "g_flFresnelReflection", value ); break;

			// Seamless Mapping
			case "$seamless_scale": SetFloat( material, "g_flSeamlessScale", value ); break;
			case "$seamless_base":
				if ( ParseBool( value ) )
					material.Set( "g_flSeamlessScale", 0.0625f );
				break;
			case "$seamless_detail":
				if ( ParseBool( value ) )
					material.Set( "g_flSeamlessDetail", 1.0f );
				break;

			// Alpha to Coverage
			case "$allowalphatocoverage":
				if ( ParseBool( value ) )
					material.Set( "g_flAllowAlphaToCoverage", 1.0f );
				break;

			// Fog Control
			case "$nofog":
				if ( ParseBool( value ) )
					material.Set( "g_flNoFog", 1.0f );
				break;

			// Teeth/Eyes
			case "$forward": SetVector( material, "g_vForward", value ); break;
			case "$illumfactor": SetFloat( material, "g_flIllumFactor", value ); break;
		}
	}

	private static void LoadAndSetTexture( Material material, string textureName, string uniformName )
	{
		if ( string.IsNullOrWhiteSpace( textureName ) )
			return;

		textureName = textureName.Replace( '\\', '/' );
		var path = $"mount://hl2/materials/{textureName}.vtex";
		var texture = Texture.Load( path, false );
		if ( texture.IsValid() && !texture.IsError )
			material.Set( uniformName, texture );
	}

	private static void SetFloat( Material material, string name, string value )
	{
		if ( float.TryParse( value, out var f ) )
			material.Set( name, f );
	}

	private static void SetInt( Material material, string name, string value )
	{
		if ( int.TryParse( value, out var i ) )
			material.Set( name, i );
	}

	private static bool ParseBool( string value )
	{
		return !string.IsNullOrEmpty( value ) && (int.TryParse( value, out var i )
			? i != 0
			: value.Equals( "true", StringComparison.OrdinalIgnoreCase ));
	}

	private static void SetBool( Material material, string name, string value )
	{
		if ( ParseBool( value ) )
			material.Set( name, 1 );
	}

	private static void SetVector( Material material, string name, string value )
	{
		var vec = ParseVector( value );
		if ( vec.HasValue )
			material.Set( name, vec.Value );
	}

	private static Vector3? ParseVector( string value )
	{
		value = value.Trim( '[', ']', '{', '}', '"', ' ' );
		var parts = value.Split( [' ', '\t'], StringSplitOptions.RemoveEmptyEntries );

		return parts.Length >= 3 && float.TryParse( parts[0], out var x ) && float.TryParse( parts[1], out var y ) && float.TryParse( parts[2], out var z )
			? new Vector3( x, y, z )
			: parts.Length == 1 && float.TryParse( parts[0], out var single )
			? new Vector3( single, single, single )
			: null;
	}
}
