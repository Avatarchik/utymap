using System;
using System.Collections;
using UnityEngine;
using UtyRx;

namespace UtyMap.Unity.Infrastructure.Reactive
{
    public static class UnityScheduler
    {
        public static void SetDefaultForUnity()
        {
#if CONSOLE
            Scheduler.MainThread = Scheduler.CurrentThread;
#else
            Scheduler.MainThread = new MainThreadScheduler();
#endif
            Scheduler.DefaultSchedulers.ConstantTimeOperations = Scheduler.Immediate;
            Scheduler.DefaultSchedulers.TailRecursion = Scheduler.Immediate;
            Scheduler.DefaultSchedulers.Iteration = Scheduler.CurrentThread;
            Scheduler.DefaultSchedulers.TimeBasedOperations = Scheduler.MainThread;
            Scheduler.DefaultSchedulers.AsyncConversions = Scheduler.ThreadPool;
        }

        class MainThreadScheduler : IScheduler, ISchedulerPeriodic, ISchedulerQueueing
        {
            readonly Action<object> scheduleAction;

            public MainThreadScheduler()
            {
                MainThreadDispatcher.Initialize();
                scheduleAction = new Action<object>(Schedule);
            }

            // delay action is run in StartCoroutine
            // Okay to action run synchronous and guaranteed run on MainThread
            IEnumerator DelayAction(TimeSpan dueTime, Action action, ICancelable cancellation)
            {
                // zero == every frame
                if (dueTime == TimeSpan.Zero)
                {
                    yield return null; // not immediately, run next frame
                }
                else
                {
                    yield return new WaitForSeconds((float)dueTime.TotalSeconds);
                }

                if (cancellation.IsDisposed) yield break;
                MainThreadDispatcher.UnsafeSend(action);
            }

            IEnumerator PeriodicAction(TimeSpan period, Action action, ICancelable cancellation)
            {
                // zero == every frame
                if (period == TimeSpan.Zero)
                {
                    while (true)
                    {
                        yield return null; // not immediately, run next frame
                        if (cancellation.IsDisposed) yield break;

                        MainThreadDispatcher.UnsafeSend(action);
                    }
                }
                else
                {
                    var seconds = (float)(period.TotalMilliseconds / 1000.0);
                    var yieldInstruction = new WaitForSeconds(seconds); // cache single instruction object

                    while (true)
                    {
                        yield return yieldInstruction;
                        if (cancellation.IsDisposed) yield break;

                        MainThreadDispatcher.UnsafeSend(action);
                    }
                }
            }

            public DateTimeOffset Now
            {
                get { return Scheduler.Now; }
            }

            void Schedule(object state)
            {
                var t = (Tuple<BooleanDisposable, Action>)state;
                if (!t.Item1.IsDisposed)
                {
                    t.Item2();
                }
            }

            public IDisposable Schedule(Action action)
            {
                var d = new BooleanDisposable();
                MainThreadDispatcher.Post(scheduleAction, Tuple.Create(d, action));
                return d;
            }

            public IDisposable Schedule(DateTimeOffset dueTime, Action action)
            {
                return Schedule(dueTime - Now, action);
            }

            public IDisposable Schedule(TimeSpan dueTime, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(dueTime);

                MainThreadDispatcher.SendStartCoroutine(DelayAction(time, action, d));

                return d;
            }

            public IDisposable SchedulePeriodic(TimeSpan period, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(period);

                MainThreadDispatcher.SendStartCoroutine(PeriodicAction(time, action, d));

                return d;
            }

            void ScheduleQueueing<T>(object state)
            {
                var t = (Tuple<ICancelable, T, Action<T>>)state;
                if (!t.Item1.IsDisposed)
                {
                    t.Item3(t.Item2);
                }
            }

            public void ScheduleQueueing<T>(ICancelable cancel, T state, Action<T> action)
            {
                MainThreadDispatcher.Post(QueuedAction<T>.Instance, Tuple.Create(cancel, state, action));
            }

            static class QueuedAction<T>
            {
                public static readonly Action<object> Instance = new Action<object>(Invoke);

                public static void Invoke(object state)
                {
                    var t = (Tuple<ICancelable, T, Action<T>>)state;

                    if (!t.Item1.IsDisposed)
                    {
                        t.Item3(t.Item2);
                    }
                }
            }
        }

        class IgnoreTimeScaleMainThreadScheduler : IScheduler, ISchedulerPeriodic, ISchedulerQueueing
        {
            readonly Action<object> scheduleAction;

            public IgnoreTimeScaleMainThreadScheduler()
            {
                MainThreadDispatcher.Initialize();
                scheduleAction = new Action<object>(Schedule);
            }

            IEnumerator DelayAction(TimeSpan dueTime, Action action, ICancelable cancellation)
            {
                if (dueTime == TimeSpan.Zero)
                {
                    yield return null;
                    if (cancellation.IsDisposed) yield break;

                    MainThreadDispatcher.UnsafeSend(action);
                }
                else
                {
                    var elapsed = 0f;
                    var dt = (float)dueTime.TotalSeconds;
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) break;

                        elapsed += Time.unscaledDeltaTime;
                        if (elapsed >= dt)
                        {
                            MainThreadDispatcher.UnsafeSend(action);
                            break;
                        }
                    }
                }
            }

            IEnumerator PeriodicAction(TimeSpan period, Action action, ICancelable cancellation)
            {
                // zero == every frame
                if (period == TimeSpan.Zero)
                {
                    while (true)
                    {
                        yield return null; // not immediately, run next frame
                        if (cancellation.IsDisposed) yield break;

                        MainThreadDispatcher.UnsafeSend(action);
                    }
                }
                else
                {
                    var elapsed = 0f;
                    var dt = (float)period.TotalSeconds;
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) break;

                        elapsed += Time.unscaledDeltaTime;
                        if (elapsed >= dt)
                        {
                            MainThreadDispatcher.UnsafeSend(action);
                            elapsed = 0;
                        }
                    }
                }
            }

            public DateTimeOffset Now
            {
                get { return Scheduler.Now; }
            }

            void Schedule(object state)
            {
                var t = (Tuple<BooleanDisposable, Action>)state;
                if (!t.Item1.IsDisposed)
                {
                    t.Item2();
                }
            }

            public IDisposable Schedule(Action action)
            {
                var d = new BooleanDisposable();
                MainThreadDispatcher.Post(scheduleAction, Tuple.Create(d, action));
                return d;
            }

            public IDisposable Schedule(DateTimeOffset dueTime, Action action)
            {
                return Schedule(dueTime - Now, action);
            }

            public IDisposable Schedule(TimeSpan dueTime, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(dueTime);

                MainThreadDispatcher.SendStartCoroutine(DelayAction(time, action, d));

                return d;
            }

            public IDisposable SchedulePeriodic(TimeSpan period, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(period);

                MainThreadDispatcher.SendStartCoroutine(PeriodicAction(time, action, d));

                return d;
            }

            public void ScheduleQueueing<T>(ICancelable cancel, T state, Action<T> action)
            {
                MainThreadDispatcher.Post(QueuedAction<T>.Instance, Tuple.Create(cancel, state, action));
            }

            static class QueuedAction<T>
            {
                public static readonly Action<object> Instance = new Action<object>(Invoke);

                public static void Invoke(object state)
                {
                    var t = (Tuple<ICancelable, T, Action<T>>)state;

                    if (!t.Item1.IsDisposed)
                    {
                        t.Item3(t.Item2);
                    }
                }
            }
        }

        class FixedUpdateMainThreadScheduler : IScheduler, ISchedulerPeriodic, ISchedulerQueueing
        {
            public FixedUpdateMainThreadScheduler()
            {
                MainThreadDispatcher.Initialize();
            }

            IEnumerator ImmediateAction<T>(T state, Action<T> action, ICancelable cancellation)
            {
                yield return null;
                if (cancellation.IsDisposed) yield break;

                MainThreadDispatcher.UnsafeSend(action, state);
            }

            IEnumerator DelayAction(TimeSpan dueTime, Action action, ICancelable cancellation)
            {
                if (dueTime == TimeSpan.Zero)
                {
                    yield return null;
                    if (cancellation.IsDisposed) yield break;

                    MainThreadDispatcher.UnsafeSend(action);
                }
                else
                {
                    var startTime = Time.fixedTime;
                    var dt = (float)dueTime.TotalSeconds;
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) break;

                        var elapsed = Time.fixedTime - startTime;
                        if (elapsed >= dt)
                        {
                            MainThreadDispatcher.UnsafeSend(action);
                            break;
                        }
                    }
                }
            }

            IEnumerator PeriodicAction(TimeSpan period, Action action, ICancelable cancellation)
            {
                // zero == every frame
                if (period == TimeSpan.Zero)
                {
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) yield break;

                        MainThreadDispatcher.UnsafeSend(action);
                    }
                }
                else
                {
                    var startTime = Time.fixedTime;
                    var dt = (float)period.TotalSeconds;
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) break;

                        var ft = Time.fixedTime;
                        var elapsed = ft - startTime;
                        if (elapsed >= dt)
                        {
                            MainThreadDispatcher.UnsafeSend(action);
                            startTime = ft;
                        }
                    }
                }
            }

            public DateTimeOffset Now
            {
                get { return Scheduler.Now; }
            }

            public IDisposable Schedule(Action action)
            {
                return Schedule(TimeSpan.Zero, action);
            }

            public IDisposable Schedule(DateTimeOffset dueTime, Action action)
            {
                return Schedule(dueTime - Now, action);
            }

            public IDisposable Schedule(TimeSpan dueTime, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(dueTime);

                MainThreadDispatcher.StartFixedUpdateMicroCoroutine(DelayAction(time, action, d));

                return d;
            }

            public IDisposable SchedulePeriodic(TimeSpan period, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(period);

                MainThreadDispatcher.StartFixedUpdateMicroCoroutine(PeriodicAction(time, action, d));

                return d;
            }

            public void ScheduleQueueing<T>(ICancelable cancel, T state, Action<T> action)
            {
                MainThreadDispatcher.StartFixedUpdateMicroCoroutine(ImmediateAction(state, action, cancel));
            }
        }

        class EndOfFrameMainThreadScheduler : IScheduler, ISchedulerPeriodic, ISchedulerQueueing
        {
            public EndOfFrameMainThreadScheduler()
            {
                MainThreadDispatcher.Initialize();
            }

            IEnumerator ImmediateAction<T>(T state, Action<T> action, ICancelable cancellation)
            {
                yield return null;
                if (cancellation.IsDisposed) yield break;

                MainThreadDispatcher.UnsafeSend(action, state);
            }

            IEnumerator DelayAction(TimeSpan dueTime, Action action, ICancelable cancellation)
            {
                if (dueTime == TimeSpan.Zero)
                {
                    yield return null;
                    if (cancellation.IsDisposed) yield break;

                    MainThreadDispatcher.UnsafeSend(action);
                }
                else
                {
                    var elapsed = 0f;
                    var dt = (float)dueTime.TotalSeconds;
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) break;

                        elapsed += Time.deltaTime;
                        if (elapsed >= dt)
                        {
                            MainThreadDispatcher.UnsafeSend(action);
                            break;
                        }
                    }
                }
            }

            IEnumerator PeriodicAction(TimeSpan period, Action action, ICancelable cancellation)
            {
                // zero == every frame
                if (period == TimeSpan.Zero)
                {
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) yield break;

                        MainThreadDispatcher.UnsafeSend(action);
                    }
                }
                else
                {
                    var elapsed = 0f;
                    var dt = (float)period.TotalSeconds;
                    while (true)
                    {
                        yield return null;
                        if (cancellation.IsDisposed) break;
                        
                        elapsed += Time.deltaTime;
                        if (elapsed >= dt)
                        {
                            MainThreadDispatcher.UnsafeSend(action);
                            elapsed = 0;
                        }
                    }
                }
            }

            public DateTimeOffset Now
            {
                get { return Scheduler.Now; }
            }

            public IDisposable Schedule(Action action)
            {
                return Schedule(TimeSpan.Zero, action);
            }

            public IDisposable Schedule(DateTimeOffset dueTime, Action action)
            {
                return Schedule(dueTime - Now, action);
            }

            public IDisposable Schedule(TimeSpan dueTime, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(dueTime);

                MainThreadDispatcher.StartEndOfFrameMicroCoroutine(DelayAction(time, action, d));

                return d;
            }

            public IDisposable SchedulePeriodic(TimeSpan period, Action action)
            {
                var d = new BooleanDisposable();
                var time = Scheduler.Normalize(period);

                MainThreadDispatcher.StartEndOfFrameMicroCoroutine(PeriodicAction(time, action, d));

                return d;
            }

            public void ScheduleQueueing<T>(ICancelable cancel, T state, Action<T> action)
            {
                MainThreadDispatcher.StartEndOfFrameMicroCoroutine(ImmediateAction(state, action, cancel));
            }
        }
    }
}