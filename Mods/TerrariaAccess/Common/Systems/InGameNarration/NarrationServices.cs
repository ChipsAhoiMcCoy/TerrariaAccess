#nullable enable
using System;
using TerrariaAccess.Common.Services;

namespace TerrariaAccess.Common.Systems;

internal sealed class DelegatedNarrationService : NarrationServiceBase
{
    private readonly Action<NarrationServiceContext> _onUpdate;
    private readonly string? _detail;

    public DelegatedNarrationService(string name, Action<NarrationServiceContext> onUpdate, string? detail = null) : base(name)
    {
        _onUpdate = onUpdate ?? throw new ArgumentNullException(nameof(onUpdate));
        _detail = detail;
    }

    public override void Update(NarrationServiceContext context)
    {
        LogTrace(context, _detail);
        if (context.TraceOnly)
        {
            return;
        }

        _onUpdate(context);
    }
}
