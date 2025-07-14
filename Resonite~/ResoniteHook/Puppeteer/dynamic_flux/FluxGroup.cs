using System.Numerics;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;

namespace nadena.dev.resonity.remote.puppeteer.dynamic_flux;

internal class FluxGroup : IFluxGroup
{
    private const float Spacing = 0.1f;
    private readonly Slot _slot;
    private bool _isVertical;

    private List<FluxGroup> _groups = new();
    
    public FluxGroup(Slot slot, bool isVertical = false)
    {
        _slot = slot;
        _isVertical = isVertical;
    }
    
    public T Spawn<T>(string? slotName = null) where T : ProtoFluxNode, new()
    {
        slotName ??= typeof(T).ToString();
        var index = _slot.ChildrenCount;
        var slot = _slot.AddSlot(slotName);
        slot.OrderOffset = index;
        var node = slot.AttachComponent<T>();
        node.EnsureVisual();

        return node;
    }

    public IFluxGroup Horizontal(string slotName = "Horizontal") {
        var index = _slot.ChildrenCount;
        var slot = _slot.AddSlot(slotName);
        slot.OrderOffset = index;

        var group = new FluxGroup(slot, false);
        _groups.Add(group);
        return group;
    }

    public IFluxGroup Vertical(string slotName = "Vertical")
    {
        var index = _slot.ChildrenCount;
        var slot = _slot.AddSlot(slotName);
        slot.OrderOffset = index;

        var group = new FluxGroup(slot, true);
        _groups.Add(group);
        return group;
    }

    public void Layout()
    {
        float3 major_axis = _isVertical ? float3.Down : float3.Right;
        float3 minor_axis = _isVertical ? float3.Right : float3.Down;

        float offset = 0;
        float widest_minor_axis = 0;

        var bounds_data = new List<(BoundingBox, Slot)>(); 
        
        foreach (var group in _groups) group.Layout();
        foreach (var child in _slot.Children)
        {
            // Compute bounding box excluding connector wires
            var boundingBox = child.ComputeBoundingBox(space: _slot, searchBlock: s => s.Name != "<WIRE_POINT>");
            var childSize = Math.Abs(Vector3.Dot(boundingBox.Size, major_axis));
            widest_minor_axis = Math.Max(widest_minor_axis, Math.Abs(Vector3.Dot(boundingBox.Size, minor_axis)));
            
            child.LocalPosition = major_axis * offset;
            offset += childSize + Spacing;
            
            bounds_data.Add((boundingBox, child));
        }
        foreach ((var boundingBox, var child) in bounds_data)
        {
            // Center the child in the minor axis
            var minorOffset = (widest_minor_axis - Math.Abs(Vector3.Dot(boundingBox.Size, minor_axis))) / 2f;
            child.LocalPosition += minor_axis * minorOffset;
        }
    }
}