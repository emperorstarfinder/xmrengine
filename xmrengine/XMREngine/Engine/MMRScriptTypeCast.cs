/***************************************************\
 *  COPYRIGHT 2010, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;


/**
 * @brief Generate script object code to perform type casting
 */

namespace OpenSim.Region.ScriptEngine.XMREngine
{

	public class TypeCast {
		private delegate void CastDelegate (ScriptMyILGen ilGen);

		private static ConstructorInfo floatConstructorStringInfo   = typeof (LSL_Float).GetConstructor (new Type[] { typeof (string) });
		private static ConstructorInfo integerConstructorStringInfo = typeof (LSL_Integer).GetConstructor (new Type[] { typeof (string) });
		private static ConstructorInfo lslFloatConstructorInfo      = typeof (LSL_Float).GetConstructor (new Type[] { typeof (float) });
		private static ConstructorInfo lslIntegerConstructorInfo    = typeof (LSL_Integer).GetConstructor (new Type[] { typeof (int) });
		private static ConstructorInfo lslListConstructorInfo       = typeof (LSL_List).GetConstructor (new Type[] { typeof (object[]) });
		private static ConstructorInfo lslStringConstructorInfo     = typeof (LSL_String).GetConstructor (new Type[] { typeof (string) });
		private static ConstructorInfo rotationConstrucorStringInfo = typeof (LSL_Rotation).GetConstructor (new Type[] { typeof (string) });
		private static ConstructorInfo vectorConstrucorStringInfo   = typeof (LSL_Vector).GetConstructor (new Type[] { typeof (string) });
		private static FieldInfo  lslFloatValueFieldInfo     = typeof (LSL_Float).GetField ("value");
		private static FieldInfo  lslIntegerValueFieldInfo   = typeof (LSL_Integer).GetField ("value");
		private static FieldInfo  lslStringValueFieldInfo    = typeof (LSL_String).GetField ("m_string");
		private static MethodInfo floatToStringMethodInfo    = typeof (TypeCast).GetMethod ("FloatToString",    new Type[] { typeof (float) });
		private static MethodInfo intToStringMethodInfo      = typeof (TypeCast).GetMethod ("IntegerToString",  new Type[] { typeof (int) });
		private static MethodInfo listToStringMethodInfo     = typeof (TypeCast).GetMethod ("ListToString",     new Type[] { typeof (LSL_List) });
		private static MethodInfo objectToStringMethodInfo   = typeof (TypeCast).GetMethod ("ObjectToString",   new Type[] { typeof (object) });
		private static MethodInfo rotationToBoolMethodInfo   = typeof (TypeCast).GetMethod ("RotationToBool",   new Type[] { typeof (LSL_Rotation) });
		private static MethodInfo rotationToStringMethodInfo = typeof (TypeCast).GetMethod ("RotationToString", new Type[] { typeof (LSL_Rotation) });
		private static MethodInfo vectorToBoolMethodInfo     = typeof (TypeCast).GetMethod ("VectorToBool",     new Type[] { typeof (LSL_Vector) });
		private static MethodInfo vectorToStringMethodInfo   = typeof (TypeCast).GetMethod ("VectorToString",   new Type[] { typeof (LSL_Vector) });
		private static MethodInfo listOfOneObjMethodInfo     = typeof (TypeCast).GetMethod ("ListOfOneObj",     new Type[] { typeof (object) });

		/*
		 * List of all allowed type casts and how to perform the casting.
		 */
		private static Dictionary<string, CastDelegate> legalTypeCasts = null;

		/**
		 * @brief create a dictionary of legal type casts.
		 * Defines what EXPLICIT type casts are allowed in addition to the IMPLICIT ones.
		 * Key is of the form <oldtype> <newtype> for IMPLICIT casting.
		 * Key is of the form <oldtype>*<newtype> for EXPLICIT casting.
		 * Value is a delegate that generates code to perform the type cast.
		 */
		public static void CreateLegalTypeCasts ()
		{
			if (legalTypeCasts == null) {
				Dictionary<string, CastDelegate> ltc = new Dictionary<string, CastDelegate> ();

				// IMPLICIT type casts (a space is in middle of the key)
				// EXPLICIT type casts (an * is in middle of the key)
				ltc.Add ("array object",    TypeCastArray2Object);
				ltc.Add ("bool float",      TypeCastBool2Float);
				ltc.Add ("bool integer",    TypeCastBool2Integer);
				ltc.Add ("bool string",     TypeCastBool2String);
				ltc.Add ("float bool",      TypeCastFloat2Bool);
				ltc.Add ("float integer",   TypeCastFloat2Integer);
				ltc.Add ("float*list",      TypeCastFloat2List);
				ltc.Add ("float object",    TypeCastFloat2Object);
				ltc.Add ("float string",    TypeCastFloat2String);
				ltc.Add ("integer bool",    TypeCastInteger2Bool);
				ltc.Add ("integer float",   TypeCastInteger2Float);
				ltc.Add ("integer*list",    TypeCastInteger2List);
				ltc.Add ("integer object",  TypeCastInteger2Object);
				ltc.Add ("integer string",  TypeCastInteger2String);
				ltc.Add ("list object",     TypeCastList2Object);
				ltc.Add ("list*string",     TypeCastList2String);
				ltc.Add ("object array",    TypeCastObject2Array);
				ltc.Add ("object float",    TypeCastObject2Float);
				ltc.Add ("object integer",  TypeCastObject2Integer);
				ltc.Add ("object list",     TypeCastObject2List);
				ltc.Add ("object rotation", TypeCastObject2Rotation);
				ltc.Add ("object string",   TypeCastObject2String);
				ltc.Add ("object vector",   TypeCastObject2Vector);
				ltc.Add ("rotation bool",   TypeCastRotation2Bool);
				ltc.Add ("rotation list",   TypeCastRotation2List);
				ltc.Add ("rotation object", TypeCastRotation2Object);
				ltc.Add ("rotation string", TypeCastRotation2String);
				ltc.Add ("string bool",     TypeCastString2Bool);
				ltc.Add ("string float",    TypeCastString2Float);
				ltc.Add ("string integer",  TypeCastString2Integer);
				ltc.Add ("string list",     TypeCastString2List);
				ltc.Add ("string object",   TypeCastString2Object);
				ltc.Add ("string rotation", TypeCastString2Rotation);
				ltc.Add ("string vector",   TypeCastString2Vector);
				ltc.Add ("vector bool",     TypeCastVector2Bool);
				ltc.Add ("vector list",     TypeCastVector2List);
				ltc.Add ("vector object",   TypeCastVector2Object);
				ltc.Add ("vector string",   TypeCastVector2String);

				//MB()
				legalTypeCasts = ltc;
			}
			//MB()
		}

		/**
		 * @brief Emit code that converts the top stack item from 'oldType' to 'newType'
		 * @param scg = what script we are compiling
		 * @param oldType = type of item currently on the stack
		 * @param newType = type to convert it to
		 * @param explicitAllowed = false: only consider implicit casts
		 *                           true: consider both implicit and explicit casts
		 * @returns with code emitted for conversion (or error message output if not allowed, and stack left unchanged)
		 */
		public static void CastTopOfStack (ScriptCodeGen scg, TokenType oldType, TokenType newType, bool explicitAllowed)
		{
			CastDelegate castDelegate;

			/*
			 * If the basic types are the same, there is no conceptual casting needed.
			 * However, there may be boxing/unboxing to/from the LSL wrappers.
			 */
			if (oldType.typ == newType.typ) {
				if (oldType.lslBoxing != newType.lslBoxing) {
					LSLUnbox (scg.ilGen, oldType);
					LSLBox (scg.ilGen, newType);
				}
				return;
			}

			/*
			 * Cast to void is always allowed, such as discarding value from 'i++' or function return value.
			 */
			if (newType is TokenTypeVoid) {
				scg.ilGen.Emit (OpCodes.Pop);
				return;
			}

			/*
			 * Some actual conversion is needed, see if it is in table of legal casts.
			 */
			string oldString = oldType.ToString ();
			string newString = newType.ToString ();

			string key = oldString + " " + newString;

			if (!legalTypeCasts.TryGetValue (key, out castDelegate)) {
				key = oldString + "*" + newString;
				if (!explicitAllowed || !legalTypeCasts.TryGetValue (key, out castDelegate)) {
					scg.ErrorMsg (newType, "illegal to cast from " + oldString + " to " + newString);
					return;
				}
			}

			/*
			 * Ok, output cast.  But make sure it is in native form without any LSL_Float-style boxing.
			 */
			LSLUnbox (scg.ilGen, oldType);
			castDelegate (scg.ilGen);
			LSLBox (scg.ilGen, newType);
		}

		/**
		 * @brief If value on the stack is an LSL-style boxed value, unbox it.
		 */
		public static void LSLUnbox (ScriptMyILGen ilGen, TokenType type)
		{
			if (type.lslBoxing == typeof (LSL_Float)) {
				ilGen.Emit (OpCodes.Ldfld, lslFloatValueFieldInfo);
			}
			if (type.lslBoxing == typeof (LSL_Integer)) {
				ilGen.Emit (OpCodes.Ldfld, lslIntegerValueFieldInfo);
			}
			if (type.lslBoxing == typeof (LSL_String)) {
				ilGen.Emit (OpCodes.Ldfld, lslStringValueFieldInfo);
			}
		}

		/**
		 * @brief If caller wants the unboxed value on stack boxed LSL-style, box it.
		 */
		private static void LSLBox (ScriptMyILGen ilGen, TokenType type)
		{
			if (type.lslBoxing == typeof (LSL_Float)) {
				ilGen.Emit (OpCodes.Newobj, lslFloatConstructorInfo);
			}
			if (type.lslBoxing == typeof (LSL_Integer)) {
				ilGen.Emit (OpCodes.Newobj, lslIntegerConstructorInfo);
			}
			if (type.lslBoxing == typeof (LSL_String)) {
				ilGen.Emit (OpCodes.Newobj, lslStringConstructorInfo);
			}
		}

		private static void TypeCastArray2Object (ScriptMyILGen ilGen)
		{
		}
		private static void TypeCastBool2Float (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Conv_R4);
		}
		private static void TypeCastBool2Integer (ScriptMyILGen ilGen)
		{
		}
		private static void TypeCastFloat2Bool (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Ldc_R4, 0.0f);
			ilGen.Emit (OpCodes.Ceq);
			ilGen.Emit (OpCodes.Ldc_I4_1);
			ilGen.Emit (OpCodes.Xor);
		}
		private static void TypeCastFloat2Integer (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Conv_I4);
		}
		private static void TypeCastFloat2Object (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (float));
		}
		private static void TypeCastInteger2Bool (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Ldc_I4_0);
			ilGen.Emit (OpCodes.Ceq);
			ilGen.Emit (OpCodes.Ldc_I4_1);
			ilGen.Emit (OpCodes.Xor);
		}
		private static void TypeCastInteger2Float (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Conv_R4);
		}
		private static void TypeCastInteger2Object (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (int));
		}
		private static void TypeCastList2Object (ScriptMyILGen ilGen)
		{
		}
		private static void TypeCastObject2Array (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Castclass, typeof (XMR_Array));
		}
		private static void TypeCastObject2Float (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Unbox_Any, typeof (float));
		}
		private static void TypeCastObject2Integer (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Unbox_Any, typeof (int));
		}
		private static void TypeCastObject2List (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Castclass, typeof (LSL_List));
		}
		private static void TypeCastObject2Rotation (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Castclass, typeof (LSL_Rotation));
		}
		private static void TypeCastObject2Vector (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Castclass, typeof (LSL_Vector));
		}
		private static void TypeCastRotation2Bool (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, rotationToBoolMethodInfo);
		}
		private static void TypeCastRotation2Object (ScriptMyILGen ilGen)
		{
		}
		private static void TypeCastString2Bool (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Ldstr, "");
			ilGen.Emit (OpCodes.Ceq);
			ilGen.Emit (OpCodes.Ldc_I4_1);
			ilGen.Emit (OpCodes.Xor);
		}
		private static void TypeCastString2Object (ScriptMyILGen ilGen)
		{
		}
		private static void TypeCastString2Rotation (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Newobj, rotationConstrucorStringInfo);
		}
		private static void TypeCastString2Vector (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Newobj, vectorConstrucorStringInfo);
		}
		private static void TypeCastVector2Bool (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, vectorToBoolMethodInfo);
		}
		private static void TypeCastVector2List (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (LSL_Vector));
			ilGen.Emit (OpCodes.Call, listOfOneObjMethodInfo);
		}
		private static void TypeCastVector2Object (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (LSL_Vector));
		}
		private static void TypeCastBool2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Newobj, rotationConstrucorStringInfo);
		}
		private static void TypeCastFloat2List (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (float));
			ilGen.Emit (OpCodes.Call, listOfOneObjMethodInfo);
		}
		private static void TypeCastFloat2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, floatToStringMethodInfo);
		}
		private static void TypeCastInteger2List (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (int));
			ilGen.Emit (OpCodes.Call, listOfOneObjMethodInfo);
		}
		private static void TypeCastInteger2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, intToStringMethodInfo);
		}
		private static void TypeCastList2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, listToStringMethodInfo);
		}
		private static void TypeCastObject2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, objectToStringMethodInfo);
		}
		private static void TypeCastRotation2List (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Box, typeof (LSL_Rotation));
			ilGen.Emit (OpCodes.Call, listOfOneObjMethodInfo);
		}
		private static void TypeCastRotation2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, rotationToStringMethodInfo);
		}
		private static void TypeCastString2Float (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Newobj, floatConstructorStringInfo);
			ilGen.Emit (OpCodes.Ldfld, lslFloatValueFieldInfo);
		}
		private static void TypeCastString2Integer (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Newobj, integerConstructorStringInfo);
			ilGen.Emit (OpCodes.Ldfld, lslIntegerValueFieldInfo);
		}
		private static void TypeCastString2List (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Newobj, lslStringConstructorInfo);
			ilGen.Emit (OpCodes.Box, typeof (LSL_String));
			ilGen.Emit (OpCodes.Call, listOfOneObjMethodInfo);
		}
		private static void TypeCastVector2String (ScriptMyILGen ilGen)
		{
			ilGen.Emit (OpCodes.Call, vectorToStringMethodInfo);
		}

		/*
		 * Because the calls are funky, let the compiler handle them.
		 */
		public static bool     RotationToBool   (LSL_Rotation x) { return !x.Equals (ScriptBaseClass.ZERO_ROTATION); }
		public static bool     VectorToBool     (LSL_Vector x)   { return !x.Equals (ScriptBaseClass.ZERO_VECTOR); }
		public static string   FloatToString    (float x)        { return x.ToString (); }
		public static string   IntegerToString  (int x)          { return x.ToString (); }
		public static string   ListToString     (LSL_List x)     { return x.ToString (); }
		public static string   ObjectToString   (object x)       { return x.ToString (); }
		public static string   RotationToString (LSL_Rotation x) { return x.ToString (); }
		public static string   VectorToString   (LSL_Vector x)   { return x.ToString (); }
		public static LSL_List ListOfOneObj     (object x)       { return new LSL_List (new object[1] { x }); }
	}
}
