using System.Numerics;
using Elements.Core;
using Renderite.Shared;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public static class PrimitiveConversions
{
    public static Vector2 Vec2(this p::Vector v)
    {
        if (v.HasZ || v.HasW)
        {
            throw new System.Exception("Invalid vector cardinality");
        }
        
        return new Vector2(v.X, v.Y);
    }
    
    public static Vector3 Vec3(this p::Vector v)
    {
        if (!v.HasZ || v.HasW)
        {
            throw new System.Exception("Invalid vector cardinality");
        }
        
        return new Vector3(v.X, v.Y, v.Z);
    }
    
    public static floatQ Quat(this p::Quaternion q)
    {
        return new floatQ(q.X, q.Y, q.Z, q.W);
    }
    
    public static Vector4 Vec4(this p::Vector v)
    {
        if (!v.HasZ || !v.HasW)
        {
            throw new System.Exception("Invalid vector cardinality");
        }
        
        return new Vector4(v.X, v.Y, v.Z, v.W);
    }
    
    public static color Color(this p::Color v)
    {
        return new color(v.R, v.G, v.B, v.A);
    }

    public static colorX ColorX(this p::Color v)
    {
        ColorProfile profile;

        if (!v.HasProfile)
        {
            profile = ColorProfile.sRGB;
        }
        else
        {
            switch (v.Profile)
            {
                case p.ColorProfile.Linear: profile = ColorProfile.Linear; break;
                case p.ColorProfile.SRgb: profile = ColorProfile.sRGB; break;
                case p.ColorProfile.SRgba: profile = ColorProfile.sRGBAlpha; break;
                default: profile = ColorProfile.sRGB; break;
            }
        }
        
        return new colorX(v.R, v.G, v.B, v.A, profile);
    }

    // ReSharper disable once InconsistentNaming
    public static float4x4 To4x4(this p::Matrix m)
    {
        return new float4x4(m.Values.ToArray());
    }
}