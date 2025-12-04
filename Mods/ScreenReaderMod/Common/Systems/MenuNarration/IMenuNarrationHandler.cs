#nullable enable
using System.Collections.Generic;

namespace ScreenReaderMod.Common.Systems.MenuNarration;

internal interface IMenuNarrationHandler
{
    bool CanHandle(MenuNarrationContext context);

    void OnMenuEntered(MenuNarrationContext context);

    void OnMenuLeft();

    IEnumerable<MenuNarrationEvent> Update(MenuNarrationContext context);
}
