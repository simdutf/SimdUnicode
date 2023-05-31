## Motivation (later)

We would like to write faster functions `TranscodeToUtf8` and `TranscodeToUtf16`. Probably, 
the most difficult and beneficial would be `TranscodeToUtf16`.


https://github.com/dotnet/runtime/blob/4d709cd12269fcbb3d0fccfb2515541944475954/src/libraries/System.Private.CoreLib/src/System/Text/Unicode/Utf8Utility.Transcoding.cs#L838



Our goal is to provide faster methods than 
https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding?view=net-7.0
