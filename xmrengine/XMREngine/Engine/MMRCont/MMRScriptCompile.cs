/***************************************************\
 *  COPYRIGHT 2009, Mike Rieker, Beverly, MA, USA  *
 *  All rights reserved.                           *
\***************************************************/

/**
 * @brief Main program for the script compiler.
 */

using System;
using System.IO;
using System.Reflection;
using log4net;
using OpenMetaverse;

namespace MMR
{
    public class ScriptCompile
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

	///??? temp haxxor ???///
        public static bool Compile (string source, 
                                    string binaryName,
                                    string assetID,
                                    TokenErrorMessage errorMessage)
	{
		throw new Exception ("COMPILE BARFO!!");
		return Compile (source, binaryName, assetID, "", errorMessage);
	}

        /**
         * @brief Compile a script to produce a .DLL file.
         * @param source = 'source' contains the whole script source
         * @param binaryName = where to write .DLL file to
         * @param assetID = the script asset ID, for error messages
         * @param errorMessage = where to write error messages to
         * @returns true: successful
         *         false: failure
         */
        public static bool Compile (string source, 
                                    string binaryName,
                                    string assetID,
                                    string debugFileName,
                                    TokenErrorMessage errorMessage)
        {
            Console.WriteLine ("MMR.ScriptCompile.Compile*: {0}", source);

            TokenBegin tokenBegin =
                        TokenBegin.Construct(errorMessage, source);

            if (tokenBegin == null)
            {
                m_log.DebugFormat("[MMR]: Tokenizing error on {0}", assetID);

                return false;
            }

            TokenScript tokenScript = ScriptReduce.Reduce(tokenBegin);

            if (tokenScript == null)
            {
                m_log.DebugFormat("[MMR]: Reducing error on {0}", assetID);

                return false;
            }

            bool ok = ScriptCodeGen.CodeGen(tokenScript, binaryName,
                    debugFileName);

            if (!ok)
                return false;

            return true;
        }
    }
}
