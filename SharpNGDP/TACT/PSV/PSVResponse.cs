﻿using System;
using System.Collections.Generic;
using System.Text;

namespace SharpNGDP.TACT.PSV
{
    public abstract class PSVResponse
    {
        public abstract bool Map(string key, string value);
    }
}