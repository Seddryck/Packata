﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Packata.Core.ResourceReading;

namespace Packata.Core;

public partial class Resource
{
    public Resource()
        => RootPath = string.Empty;

    public Resource(string rootPath)
        => RootPath = rootPath;

    /// <summary>
    /// The root path of the data package.
    /// </summary> 
    public string RootPath { get; }
}
