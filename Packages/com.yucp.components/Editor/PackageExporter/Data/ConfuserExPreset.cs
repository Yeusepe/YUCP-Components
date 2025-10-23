namespace YUCP.Components.Editor.PackageExporter
{
    /// <summary>
    /// Obfuscation protection levels for ConfuserEx.
    /// Each preset defines a different balance between protection strength and compatibility.
    /// </summary>
    public enum ConfuserExPreset
    {
        Mild,
        Normal,
        Aggressive
    }

    /// <summary>
    /// Generates ConfuserEx protection configurations based on preset level.
    /// </summary>
    public static class ConfuserExPresetGenerator
    {
        public static string GenerateProtectionRules(ConfuserExPreset preset)
        {
            switch (preset)
            {
                case ConfuserExPreset.Mild:
                    return @"
    <!-- Mild protection - basic name obfuscation and string encryption -->
    <protection id=""rename"">
      <argument name=""mode"" value=""letters"" />
      <argument name=""renEnum"" value=""true"" />
    </protection>
    
    <protection id=""constants"">
      <argument name=""mode"" value=""normal"" />
      <argument name=""decoderCount"" value=""3"" />
    </protection>";

                case ConfuserExPreset.Normal:
                    return @"
    <!-- Normal protection - UNITY COMPATIBLE (NO anti-tamper) -->
    <protection id=""anti ildasm"" />
    
    <protection id=""rename"">
      <argument name=""mode"" value=""letters"" />
      <argument name=""renEnum"" value=""true"" />
    </protection>
    
    <protection id=""constants"">
      <argument name=""mode"" value=""normal"" />
      <argument name=""decoderCount"" value=""5"" />
    </protection>";

                case ConfuserExPreset.Aggressive:
                    return @"
    <!-- Aggressive protection - maximum obfuscation (may affect performance) -->
    <protection id=""anti ildasm"" />
    
    <protection id=""anti tamper"">
      <argument name=""key"" value=""dynamic"" />
    </protection>
    
    <protection id=""anti debug"" />
    
    <protection id=""anti dump"" />
    
    <protection id=""ctrl flow"">
      <argument name=""type"" value=""switch"" />
      <argument name=""predicate"" value=""expression"" />
    </protection>
    
    <protection id=""ref proxy"">
      <argument name=""mode"" value=""strong"" />
      <argument name=""typeErasure"" value=""true"" />
      <argument name=""depth"" value=""5"" />
    </protection>
    
    <protection id=""rename"">
      <argument name=""mode"" value=""unicode"" />
      <argument name=""renEnum"" value=""true"" />
      <argument name=""renameArgs"" value=""true"" />
      <argument name=""flatten"" value=""true"" />
    </protection>
    
    <protection id=""constants"">
      <argument name=""mode"" value=""dynamic"" />
      <argument name=""decoderCount"" value=""10"" />
    </protection>
    
    <protection id=""resources"">
      <argument name=""mode"" value=""dynamic"" />
    </protection>";

                default:
                    return GenerateProtectionRules(ConfuserExPreset.Normal);
            }
        }

        public static string GetPresetDescription(ConfuserExPreset preset)
        {
            switch (preset)
            {
                case ConfuserExPreset.Mild:
                    return "Basic protection - Renames symbols and encrypts strings. Fast and compatible.";
                
                case ConfuserExPreset.Normal:
                    return "Recommended protection - Full obfuscation with control flow and anti-tampering. Good balance.";
                
                case ConfuserExPreset.Aggressive:
                    return "Maximum protection - All features enabled. May impact performance and compatibility.";
                
                default:
                    return "";
            }
        }
    }
}

