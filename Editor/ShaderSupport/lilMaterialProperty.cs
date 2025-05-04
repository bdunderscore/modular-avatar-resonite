/*
 * MIT License
 * 
 * Copyright (c) 2020-2024 lilxyzw
 * Copyright (c) 2025 bd_
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using lilToon;
using UnityEditor;
using UnityEngine;

namespace nadena.dev.ndmf.platform.resonite
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal class lilMaterialProperty
    {
        private LiltoonShaderSupport? _liltoonSupport;
        private int _nameID;
        
        public string propertyName { get; }

        private Material Material
        {
            get { return _liltoonSupport?.Material ?? throw new System.NullReferenceException(); }
        }
        
        public float floatValue
        {
            get { return Material.GetFloat(_nameID); }
            set { Material.SetFloat(_nameID, value); }
        }

        public Vector4 vectorValue
        {
            get { return Material.GetVector(_nameID); }
            set { Material.SetVector(_nameID, value); }
        }

        public Color colorValue
        {
            get { return Material.GetColor(_nameID); }
            set { Material.SetColor(_nameID, value); }
        }

        public Texture textureValue
        {
            get { return Material.GetTexture(_nameID); }
            set { Material.SetTexture(_nameID, value); }
        }

        // Other
        public string name => propertyName;

        public string displayName => propertyName;

        public lilMaterialProperty(string name, params PropertyBlock[] inBrocks)
        {
            propertyName = name;
            _nameID = Shader.PropertyToID(name);
        }

        public lilMaterialProperty(string name, bool isTex, params PropertyBlock[] inBrocks)
        {
            propertyName = name;
            _nameID = Shader.PropertyToID(name);
        }

        public void Bind(LiltoonShaderSupport liltoonSupport)
        {
            _liltoonSupport = liltoonSupport;
        }
    }
}