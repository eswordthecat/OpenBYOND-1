﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenBYOND
{
    public static class StringExtensions
    {
        /// <summary>
        /// Python-style join.
        /// </summary>
        /// <param name="separator"></param>
        /// <param name="E"></param>
        /// <returns></returns>
        public static string join(this string separator, IEnumerable<string> E)
        {
            return string.Join(separator, E);
        }
    }
}
