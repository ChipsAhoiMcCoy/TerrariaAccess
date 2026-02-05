#nullable enable
using System;
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

/// <summary>
/// Legacy interface for menu narration handlers.
/// New code should use <see cref="Handlers.IMenuHandler"/> instead.
/// </summary>
[Obsolete("Use ScreenReaderMod.Common.Systems.MenuNarration.Handlers.IMenuHandler instead")]
internal interface IMenuNarrationHandler
{
    bool CanHandle(MenuNarrationContext context);

    void OnMenuEntered(MenuNarrationContext context);

    void OnMenuLeft();

    IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context);
}
