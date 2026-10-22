using System;
namespace SampleApp
{
	public class Services
	{
        public static string CleanWord(string input)
        {
       
            string variableName = "";

            // Check if first character is a letter or underscore
            if (char.IsLetter(input[0]) || input[0] == '_')
            {
                variableName += input[0];
            }
            else
            {
                // If not, add an underscore at the beginning
                string hexValue = string.Format("_0x{0:X2}_", (int)input[0]);
                variableName += hexValue;
            }

            // Add the rest of the characters, replacing invalid characters with encrypted versions
            for (int i = 1; i < input.Length; i++)
            {
                if (char.IsLetterOrDigit(input[i]) || input[i] == '_')
                {
                    variableName += input[i];
                }
                else
                {
                    // If the character is not valid, replace it with an encrypted version
                    string hexValue = string.Format("_0x{0:X2}_", (int)input[i]);
                    variableName += hexValue;
                }
            }

            // Return the formatted variable name
            return variableName;
        }
    }
}

