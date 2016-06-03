﻿using System;

/*
    Class that stores a given amount of last GPS data points for a TimeSource
    to use. Decides whether the GPS data is valid based on the GPS message, and
    whether each GPS time is more than threshold seconds late than the previously
    observed data.
*/

namespace Utils.GPS
{    
    public class NaiveTimeStorage : ITimeStorage
    {
        /* Default time source parameters */
        /* Use the GPS data from up to 2 minutes ago */
        const int maxHistoryDefault = 120;

        /* Point rejection threshold is 50 us */
        const double thresholdDefault = 50 * 1e-6;
        
        /* Compare each new GPS point to 5 previous points*/
        const int lookbackDefault = 5;

        /* After how many seconds the point is considered to be
        "stale" and marked as invalid */
        const int invalidateAfterDefault = 120;

        /* How many previous GPS points to store. */
        public int maxHistory
        {
            get; private set;
        }

        /* Stores the GPS data history */
        public GpsData[] history
        {
            get; private set;
        }

        /* After how many seconds the point is considered to be
        "stale" and marked as invalid */
        public int invalidateAfter
        {
            get; private set;
        }

        /* If the point arrives later than this many ticks, it's invalid */
        public long threshold
        { 
            get; private set;
        }

        /* Stopwatch freqeuncy */
        public long frequency
        {
            get; private set;
        }

        /* How many previous points should within the threshold of the new one
        for it to be counted as valid */
        public int lookback
        {
            get; private set;
        }

        /* How many GPS points have been received up to now. */
        public long pointsReceived
        {
            get; private set;
        } = 0;

        /* Timer for old point invalidation */
        ITimer timer;

        /* System.Stopwatch */
        IStopwatch stopwatch;

        /* Create a new threshold TimeSource given every parameter. */
        public NaiveTimeStorage(double threshold, int maxHistory, int lookback,
            int invalidateAfter)
        {
            Validate(threshold, maxHistory, lookback, invalidateAfter);
            timer = new TimerWrapper();
            stopwatch = new StopwatchWrapper();
            Initialize(threshold, maxHistory, lookback, invalidateAfter);
        }
        
        /* Create a new threshold TimeSource with default parameters and
        custom Stopwatch and Timer. */
        public NaiveTimeStorage(IStopwatch stopwatch, ITimer timer)
        {
            Validate(thresholdDefault, maxHistoryDefault, lookbackDefault,
                invalidateAfterDefault);
            this.timer = timer;
            this.stopwatch = stopwatch;
            Initialize(thresholdDefault, maxHistoryDefault, lookbackDefault,
                invalidateAfterDefault);
        }

        /* Creates a TimeSource with default parameters and default
        Stopwatch and Timer */
        public NaiveTimeStorage()
        {
            Validate(thresholdDefault, maxHistoryDefault, lookbackDefault,
                invalidateAfterDefault);
            timer = new TimerWrapper();
            stopwatch = new StopwatchWrapper();
            Initialize(thresholdDefault, maxHistoryDefault, lookbackDefault,
                invalidateAfterDefault);
        }

        /* Create a new threshold TimeSource with custom parameters and
        custom Stopwatch and Timer. */
        public NaiveTimeStorage(double threshold, int maxHistory, int lookback,
            int invalidateAfter, IStopwatch stopwatch, ITimer timer)
        {
            Validate(threshold, maxHistory, lookback, invalidateAfter);            
            this.timer = timer;
            this.stopwatch = stopwatch;
            Initialize(threshold, maxHistory, lookback, invalidateAfter);
        }

        /* Checks that the parameters are within permitted range */
        private void Validate(double threshold, int maxHistory,
            int lookback, int invalidateAfter)
        {

            if (threshold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "TimeSource threshold can't be negative");
            }

            if (maxHistory < 2)
            {
                throw new ArgumentOutOfRangeException(
                    "TimeSource history length can't be less than two");
            }

            if (lookback < 1)
            {
                throw new ArgumentOutOfRangeException(
                    "TimeSource lookback can't be less than one");
            }

            if (lookback > maxHistory)
            {
                throw new ArgumentOutOfRangeException(
                    "TimeSource lookback can't be larger than history");
            }

            if (invalidateAfter < 0)
            {
                throw new ArgumentOutOfRangeException(
                   "TimeSource invalidation timer interval can't be negative");
            }
        }

        /* Initialize the storage parameters */
        private void Initialize(double threshold, int maxHistory, int lookback,
            int invalidateAfter)
        {
            frequency = stopwatch.Frequency;
            this.maxHistory = maxHistory;
            this.lookback = lookback;
            this.invalidateAfter = invalidateAfter;            
            this.threshold = Convert.ToInt64(threshold * frequency);
            history = new GpsData[maxHistory];

            lock (history)
            {                
                for (int i = 0; i < maxHistory; i++)
                {
                    history[i].valid = false;
                }
            }
            ScheduleOldPointInvalidation();
        }

        /* Schedules old data point invalidation in background */
        private void ScheduleOldPointInvalidation()
        {
            timer.Interval = invalidateAfter * 1000;
            timer.Elapsed += InvalidateStaleData;
            timer.AutoReset = true;
            timer.Enabled = true;
        }

        /* Invalidates every data point that arrived later than 
        invalidateAfter seconds ago */
        private void InvalidateStaleData(object sender, 
            System.Timers.ElapsedEventArgs e)
        {
            var now = stopwatch.GetTimestamp();
            lock (history)
            {
                for (int i = 0; i < history.Length; i++)
                {
                    var delta = Convert.ToDouble(now - history[i].ticks) 
                        / Convert.ToDouble(frequency);
                    if (delta > invalidateAfter)
                    {
                        history[i].valid = false;
                    }
                }
            }
        }

        /* Returns the number of valid points currently stored. */
        public int ValidPointsCount { get { return GetValidPointsCount(); } }

        /* Stores a new point, returns true if the point is valid and false 
        otherwise. */
        public bool Store(GpsData data)
        {
            if (!data.valid)
            {                
                return false; // GPS receiver said this point is invalid
            }

            bool valid = IsPointValid(data);
            pointsReceived += 1;
            SavePoint(data, valid);

            if (pointsReceived < lookback)
            {
                return false;
            }

            return valid;
        }

        /* Checks the point against other points in history */
        private bool IsPointValid(GpsData data)
        {
            bool valid;
            if (pointsReceived < lookback)
            {
                valid = false;
            }
            else
            {
                valid = IsWithinThresholdAllPoints(data);
            }
            return valid;
        }

        /* Checks the data point against the history within lookback */
        private bool IsWithinThresholdAllPoints(GpsData data)
        {
            lock (history)
            {
                for (int i = 0; i < lookback; i++)
                {
                    int secondsDifference = Convert.ToInt32(Math.Round(
                        (data.timestamp - history[i].timestamp)
                        .TotalSeconds));
                    if (!IsWithinThresholdThisPoint(data, history[i], 
                        secondsDifference))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /* Returns true if the point from dt seconds ago is within 
           the threshold */
        private bool IsWithinThresholdThisPoint(GpsData newPoint, 
            GpsData oldPoint, int secondsDifference)
        {
            long delta = newPoint.ticks - oldPoint.ticks;
            return delta - frequency * secondsDifference <= threshold;
        }

        /* Stores the given GPS data into the buffer. */
        private void SavePoint(GpsData data, bool valid)
        {
            lock (history)
            {
                for (int i = history.Length - 1; i > 0; i--)
                {
                    history[i] = history[i - 1];
                }
                history[0] = data;
                history[0].valid = valid;
            }
        }        

        /* Returns the number of valid points stored in history */
        private int GetValidPointsCount()
        {
            int num = 0;
            lock (history)
            {
                foreach (var point in history)
                {
                    if (point.valid)
                    {
                        num++;
                    }
                }
            }
            return num;
        }

        /* Returns valid points from history */
        public GpsData[] GetValidPoints()
        {
            var result = new GpsData[GetValidPointsCount()];
            int i = 0;
            lock (history)
            {
                foreach (var point in history)
                {
                    if (point.valid)
                    {
                        result[i] = point;
                        i++;
                    }
                }
            }
            return result;
        }
    }
}
