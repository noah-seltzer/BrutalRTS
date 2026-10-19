namespace RTS.Game;

/// <summary>
/// Credits text required by the BRUTAL License v1.0 (clause 2.1: copyright notice in the credits
/// screen; clause 2.3: NOTICE.txt distributed alongside the product — copied to the output dir by
/// RTS.Game.csproj together with LICENSES/*.txt).
/// </summary>
public static class Credits
{
    public const string BrutalCopyright = "BRUTAL game development framework is Copyright (c) 2024 Ahwoo Limited.";

    public static readonly string[] Lines =
    {
        BrutalCopyright,
        "This product is built with BRUTAL, a game development framework by Ahwoo Limited,",
        "used under the BRUTAL License v1.0.  www.ahwoo.com/brutal",
        "",
        "Third-party components (GLFW, Dear ImGui, GLAD/OpenGL) - see LICENSES/ next to the executable.",
    };
}
