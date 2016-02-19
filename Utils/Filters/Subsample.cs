﻿using System;

namespace Utils.Filters
{
    public class Subsample : AbstractSimpleFilter
    {
        int skipped;
        int skipmax;

        /* Creates a downsampling filter that returns one in every ratio points. */
        public Subsample(int ratio)
        {
            if (ratio < 1)
            {
                var msg = "Filter ratio can't be smaller than one";
                throw new ArgumentOutOfRangeException(msg);
            }
            skipmax = ratio;
            skipped = 0;
        }

        override protected double[] Filter(double[] data)
        {
            var result = new double[(data.Length + skipped) / skipmax];
            int j = 0;
            foreach (var x in data)
            {
                skipped++;
                if (skipped == skipmax)
                {
                    skipped = 0;
                    result[j] = x;
                    j++;
                }
            }
            return result;
        }
    }
}
