#region Copyright (c) Roni Schuetz - All Rights Reserved
// * --------------------------------------------------------------------- *
// *                              Roni Schuetz                             *
// *              Copyright (c) 2008 All Rights reserved                   *
// *                                                                       *
// * Shared Cache high-performance, distributed caching and    *
// * replicated caching system, generic in nature, but intended to         *
// * speeding up dynamic web and / or win applications by alleviating      *
// * database load.                                                        *
// *                                                                       *
// * This Software is written by Roni Schuetz (schuetz AT gmail DOT com)   *
// *                                                                       *
// * This library is free software; you can redistribute it and/or         *
// * modify it under the terms of the GNU Lesser General Public License    *
// * as published by the Free Software Foundation; either version 2.1      *
// * of the License, or (at your option) any later version.                *
// *                                                                       *
// * This library is distributed in the hope that it will be useful,       *
// * but WITHOUT ANY WARRANTY; without even the implied warranty of        *
// * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU      *
// * Lesser General Public License for more details.                       *
// *                                                                       *
// * You should have received a copy of the GNU Lesser General Public      *
// * License along with this library; if not, write to the Free            *
// * Software Foundation, Inc., 59 Temple Place, Suite 330,                *
// * Boston, MA 02111-1307 USA                                             *
// *                                                                       *
// *       THIS COPYRIGHT NOTICE MAY NOT BE REMOVED FROM THIS FILE.        *
// * --------------------------------------------------------------------- *
#endregion 

// *************************************************************************
//
// Name:      SharedCacheThreadPool.cs
// 
// Created:   10-02-2008 SharedCache.com, rschuetz
// Modified:  10-02-2008 SharedCache.com, rschuetz : Creation
// Modified:  28-01-2010 SharedCache.com, chrisme  : clean up code
// ************************************************************************* 

#region Comments by Mike Woodring about the ability of the threadpool
// ThreadPool.cs
//
// This file defines a custom ThreadPool class that supports the following
// characteristics (property and method names shown in []):
//
// * can be explicitly started and stopped (and restarted) [Start,Stop,StopAndWait]
//
// * configurable thread priority [Priority]
//
// * configurable foreground/background characteristic [IsBackground]
//
// * configurable minimum thread count (called 'static' or 'permanent' threads) [constructor]
//
// * configurable maximum thread count (threads added over the minimum are
//   called 'dynamic' threads) [constructor, MaxThreadCount]
//
// * configurable dynamic thread creation trigger (the point at which
//   the pool decides to add new threads) [NewThreadTrigger]
//
// * configurable dynamic thread decay interval (the time period
//   after which an idle dynamic thread will exit) [DynamicThreadDecay]
//
// * configurable limit (optional) to the request queue size (by default unbounded) [RequestQueueLimit]
//
// * pool extends WaitHandle, becomes signaled when last thread exits [StopAndWait, WaitHandle methods]
//
// * operations enqueued to the pool are cancellable [IWorkRequest returned by PostRequest]
//
// * enqueue operation supports early bound approach (ala ThreadPool.QueueUserWorkItem)
//   as well as late bound approach (ala Control.Invoke/BeginInvoke) to posting work requests [PostRequest]
//
// * optional propogation of calling thread call context to target [PropogateCallContext]
//
// * optional propogation of calling thread principal to target [PropogateThreadPrincipal]
//
// * optional propogation of calling thread HttpContext to target [PropogateHttpContext]
//
// * support for started/stopped event subscription & notification [Started, Stopped]
//
// Known issues/limitations/comments:
//
// * The PropogateCASMarkers property exists for future support for propogating
//   the calling thread's installed CAS markers in the same way that the built-in thread
//   pool does.  Currently, there is no support for user-defined code to perform that
//   operation.
//
// * PropogateCallContext and PropogateHttpContext both use reflection against private
//   members to due their job.  As such, these two properties are set to false by default,
//   but do work on the first release of the framework (including .NET Server) and its
//   service packs.  These features have not been tested on Everett at this time.
//
// Mike Woodring
// http://staff.develop.com/woodring
//
#endregion Mike Woodring

using System;
using System.Threading;
using System.Collections;
using System.Diagnostics;
using System.Security.Principal;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Web;

namespace SharedCache.WinServiceCommon.Threading
{
    /// <summary>
    /// 
    /// </summary>
	public delegate void SharedCacheWorkRequestDelegate(object state, DateTime requestEnqueueTime);
    /// <summary>
    /// 
    /// </summary>
	public delegate void SharedCacheThreadPoolDelegate();

	#region ISharedCacheWorkRequest Interface
	///<summary>
	///</summary>
	public interface ISharedCacheWorkRequest
	{
		///<summary>
		///</summary>
		///<returns></returns>
		bool Cancel();
	}
	#endregion

	/// <summary>
	/// This defines a custom ThreadPool class that supports the following characteristics (property and method names shown in []):
	///
	/// * can be explicitly started and stopped (and restarted) [Start,Stop,StopAndWait]
	///
	/// * configurable thread priority [Priority]
	///
	/// * configurable foreground/background characteristic [IsBackground]
	///
	/// * configurable minimum thread count (called 'static' or 'permanent' threads) [constructor]
	///
	/// * configurable maximum thread count (threads added over the minimum are
	///   called 'dynamic' threads) [constructor, MaxThreadCount]
	///
	/// * configurable dynamic thread creation trigger (the point at which
	///   the pool decides to add new threads) [NewThreadTrigger]
	///
	/// * configurable dynamic thread decay interval (the time period
	///   after which an idle dynamic thread will exit) [DynamicThreadDecay]
	///
	/// * configurable limit (optional) to the request queue size (by default unbounded) [RequestQueueLimit]
	///
	/// * pool extends WaitHandle, becomes signaled when last thread exits [StopAndWait, WaitHandle methods]
	///
	/// * operations enqueued to the pool are cancellable [IWorkRequest returned by PostRequest]
	///
	/// * enqueue operation supports early bound approach (ala ThreadPool.QueueUserWorkItem)
	///   as well as late bound approach (ala Control.Invoke/BeginInvoke) to posting work requests [PostRequest]
	///
	/// * optional propogation of calling thread call context to target [PropogateCallContext]
	///
	/// * optional propogation of calling thread principal to target [PropogateThreadPrincipal]
	///
	/// * optional propogation of calling thread HttpContext to target [PropogateHttpContext]
	///
	/// * support for started/stopped event subscription + notification [Started, Stopped]
	///
	/// Known issues/limitations/comments:
	///
	/// * The PropogateCASMarkers property exists for future support for propogating
	///   the calling thread's installed CAS markers in the same way that the built-in thread
	///   pool does.  Currently, there is no support for user-defined code to perform that
	///   operation.
	///
	/// * PropogateCallContext and PropogateHttpContext both use reflection against private
	///   members to due their job.  As such, these two properties are set to false by default,
	///   but do work on the first release of the framework (including .NET Server) and its
	///   service packs.  These features have not been tested on Everett at this time.
	/// </summary>
	public class SharedCacheThreadPool : WaitHandle
	{
		#region ThreadPool constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SharedCacheThreadPool"/> class.
        /// </summary>
        /// <param name="initialThreadCount">The initial thread count.</param>
        /// <param name="maxThreadCount">The max thread count.</param>
        /// <param name="poolName">Name of the pool.</param>
		public SharedCacheThreadPool(int initialThreadCount, int maxThreadCount, string poolName)
			: this(initialThreadCount, maxThreadCount, poolName,
								DEFAULT_NEW_THREAD_TRIGGER_TIME,
								DEFAULT_DYNAMIC_THREAD_DECAY_TIME,
								DEFAULT_THREAD_PRIORITY,
								DEFAULT_REQUEST_QUEUE_LIMIT)
		{}

		///<summary>
		///</summary>
		///<param name="initialThreadCount"></param>
		///<param name="maxThreadCount"></param>
		///<param name="poolName"></param>
		///<param name="newThreadTrigger"></param>
		///<param name="dynamicThreadDecayTime"></param>
		///<param name="threadPriority"></param>
		///<param name="requestQueueLimit"></param>
		///<exception cref="ArgumentException"></exception>
		///<exception cref="ArgumentNullException"></exception>
		public SharedCacheThreadPool(int initialThreadCount, int maxThreadCount, string poolName,
													 int newThreadTrigger, int dynamicThreadDecayTime,
													 ThreadPriority threadPriority, int requestQueueLimit)
		{

#if DEBUG
			Debug.WriteLine(string.Format("creating new thread pool {0}:", poolName));
			Debug.WriteLine(string.Format("  initial thread count:      {0}", initialThreadCount));
			Debug.WriteLine(string.Format("  max thread count:          {0}", maxThreadCount));
			Debug.WriteLine(string.Format("  new thread trigger:        {0} ms", newThreadTrigger));
			Debug.WriteLine(string.Format("  dynamic thread decay time: {0} ms", dynamicThreadDecayTime));
			Debug.WriteLine(string.Format("  request queue limit:       {0} entries", requestQueueLimit));
			
			Handler.LogHandler.Info(string.Format("creating new thread pool {0}:", poolName));
			Handler.LogHandler.Info(string.Format("  initial thread count:      {0}", initialThreadCount));
			Handler.LogHandler.Info(string.Format("  max thread count:          {0}", maxThreadCount));
			Handler.LogHandler.Info(string.Format("  new thread trigger:        {0} ms", newThreadTrigger));
			Handler.LogHandler.Info(string.Format("  dynamic thread decay time: {0} ms", dynamicThreadDecayTime));
			Handler.LogHandler.Info(string.Format("  request queue limit:       {0} entries", requestQueueLimit));
#endif

			SafeWaitHandle = stopCompleteEvent.SafeWaitHandle;

			if (maxThreadCount < initialThreadCount)
			{
				throw new ArgumentException("Maximum thread count must be >= initial thread count.", "maxThreadCount");
			}

			if (dynamicThreadDecayTime <= 0)
			{
				throw new ArgumentException("Dynamic thread decay time cannot be <= 0.", "dynamicThreadDecayTime");
			}

			if (newThreadTrigger <= 0)
			{
				throw new ArgumentException("New thread trigger time cannot be <= 0.", "newThreadTrigger");
			}

			this.initialThreadCount = initialThreadCount;
			this.maxThreadCount = maxThreadCount;
			this.requestQueueLimit = (requestQueueLimit < 0 ? DEFAULT_REQUEST_QUEUE_LIMIT : requestQueueLimit);
			this.decayTime = dynamicThreadDecayTime;
			this.newThreadTrigger = new TimeSpan(TimeSpan.TicksPerMillisecond * newThreadTrigger);
			this.threadPriority = threadPriority;
			this.requestQueue = new Queue(requestQueueLimit < 0 ? 4096 : requestQueueLimit);

			if (poolName == null)
			{
				throw new ArgumentNullException("poolName", "Thread pool name cannot be null");
			}
		    this.threadPoolName = poolName;
		}

		#endregion

		#region ThreadPool properties
		// The Priority & DynamicThreadDecay properties are not thread safe
		// and can only be set before Start is called.
		//
        /// <summary>
        /// Gets or sets the priority.
        /// </summary>
        /// <value>The priority.</value>
		public ThreadPriority Priority
		{
			get { return (threadPriority); }

			set
			{
				if (hasBeenStarted)
				{
					throw new InvalidOperationException("Cannot adjust thread priority after pool has been started.");
				}

				threadPriority = value;
			}
		}

        /// <summary>
        /// Gets or sets the dynamic thread decay.
        /// </summary>
        /// <value>The dynamic thread decay.</value>
		public int DynamicThreadDecay
		{
			get { return (decayTime); }

			set
			{
				if (hasBeenStarted)
				{
					throw new InvalidOperationException("Cannot adjust dynamic thread decay time after pool has been started.");
				}

				if (value <= 0)
				{
					throw new ArgumentException("Dynamic thread decay time cannot be <= 0.", "value");
				}

				decayTime = value;
			}
		}

        /// <summary>
        /// Gets or sets the new thread trigger.
        /// </summary>
        /// <value>The new thread trigger.</value>
		public int NewThreadTrigger
		{
			get { return ((int)newThreadTrigger.TotalMilliseconds); }

			set
			{
				if (value <= 0)
				{
					throw new ArgumentException("New thread trigger time cannot be <= 0.", "value");
				}

				lock (this)
				{
					newThreadTrigger = new TimeSpan(TimeSpan.TicksPerMillisecond * value);
				}
			}
		}

        /// <summary>
        /// Gets or sets the request queue limit.
        /// </summary>
        /// <value>The request queue limit.</value>
		public int RequestQueueLimit
		{
			get { return (requestQueueLimit); }
			set { requestQueueLimit = (value < 0 ? DEFAULT_REQUEST_QUEUE_LIMIT : value); }
		}

        /// <summary>
        /// Gets the available threads.
        /// </summary>
        /// <value>The available threads.</value>
		public int AvailableThreads
		{
			get { return (maxThreadCount - currentThreadCount); }
		}

		///<summary>
		///</summary>
		///<exception cref="ArgumentException"></exception>
		public int MaxThreads
		{
			get { return (maxThreadCount); }

			set
			{
				if (value < initialThreadCount)
				{
					throw new ArgumentException("Maximum thread count must be >= initial thread count.", "MaxThreads");
				}

				maxThreadCount = value;
			}
		}

		///<summary>
		///</summary>
		public bool IsStarted
		{
			get { return (hasBeenStarted); }
		}

		///<summary>
		///</summary>
		public bool PropogateThreadPrincipal
		{
			get { return (propogateThreadPrincipal); }
			set { propogateThreadPrincipal = value; }
		}

		///<summary>
		///</summary>
		public bool PropogateCallContext
		{
			get { return (propogateCallContext); }
			set { propogateCallContext = value; }
		}

		///<summary>
		///</summary>
		public bool PropogateHttpContext
		{
			get { return (propogateHttpContext); }
			set { propogateHttpContext = value; }
		}

		///<summary>
		///</summary>
		public bool PropogateCASMarkers
		{
			get { return (propogateCASMarkers); }

			// When CompressedStack get/set is opened up,
			// add the following setter back in.
			//
			// set { propogateCASMarkers = value; }
		}

		///<summary>
		///</summary>
		///<exception cref="InvalidOperationException"></exception>
		public bool IsBackground
		{
			get { return (useBackgroundThreads); }

			set
			{
				if (hasBeenStarted)
				{
					throw new InvalidOperationException("Cannot adjust background status after pool has been started.");
				}

				useBackgroundThreads = value;
			}
		}
		#endregion

		#region ThreadPool events
		///<summary>
		///</summary>
		public event SharedCacheThreadPoolDelegate Started;
		///<summary>
		///</summary>
		public event SharedCacheThreadPoolDelegate Stopped;
		#endregion

		///<summary>
		///</summary>
		///<exception cref="InvalidOperationException"></exception>
		public void Start()
		{
			lock (this)
			{
				if (hasBeenStarted)
				{
					throw new InvalidOperationException("Pool has already been started.");
				}

				hasBeenStarted = true;

				// Check to see if there were already items posted to the queue
				// before Start was called.  If so, reset their timestamps to
				// the current time.
				//
				if (requestQueue.Count > 0)
				{
					ResetWorkRequestTimes();
				}

				for (int n = 0; n < initialThreadCount; n++)
				{
					var thread = new ThreadWrapper(this, true, threadPriority, string.Format("{0} (static)", threadPoolName));
					thread.Start();
				}

				if (Started != null)
				{
					Started(); // TODO: reconsider firing this event while holding the lock...
				}
			}
		}


		#region ThreadPool.Stop and InternalStop

        /// <summary>
        /// Stops this instance.
        /// </summary>
		public void Stop()
		{
			InternalStop(false, Timeout.Infinite);
		}

        /// <summary>
        /// Stops the and wait.
        /// </summary>
		public void StopAndWait()
		{
			InternalStop(true, Timeout.Infinite);
		}

        /// <summary>
        /// Stops the and wait.
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns></returns>
		public bool StopAndWait(int timeout)
		{
			return InternalStop(true, timeout);
		}

		private bool InternalStop(bool wait, int timeout)
		{
			if (!hasBeenStarted)
			{
				throw new InvalidOperationException("Cannot stop a thread pool that has not been started yet.");
			}

			lock (this)
			{
                Debug.WriteLine(string.Format("[{0}, {1}] Stopping pool (# threads = {2})", Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name, currentThreadCount));
				stopInProgress = true;
				Monitor.PulseAll(this);
			}

			if (wait)
			{
				bool stopComplete = WaitOne(timeout, true);

				if (stopComplete)
				{
					// If the stop was successful, we can support being
					// to be restarted.  If the stop was requested, but not
					// waited on, then we don't support restarting.
					//
					hasBeenStarted = false;
					stopInProgress = false;
					requestQueue.Clear();
					stopCompleteEvent.Reset();
				}

				return (stopComplete);
			}

			return (true);
		}

		#endregion

		#region ThreadPool.PostRequest(early bound)

		/// <summary>
        /// Overloads for the early bound WorkRequestDelegate-based targets.
        /// </summary>
        /// <param name="cb">The cb.</param>
        /// <returns></returns>
		public bool PostRequest(SharedCacheWorkRequestDelegate cb)
		{
			return PostRequest(cb, (object)null);
		}

        /// <summary>
        /// Posts the request.
        /// </summary>
        /// <param name="cb">The cb.</param>
        /// <param name="state">The state.</param>
        /// <returns></returns>
		public bool PostRequest(SharedCacheWorkRequestDelegate cb, object state)
		{
			ISharedCacheWorkRequest notUsed;
			return PostRequest(cb, state, out notUsed);
		}

        /// <summary>
        /// Posts the request.
        /// </summary>
        /// <param name="cb">The cb.</param>
        /// <param name="state">The state.</param>
        /// <param name="reqStatus">The req status.</param>
        /// <returns></returns>
		public bool PostRequest(SharedCacheWorkRequestDelegate cb, object state, out ISharedCacheWorkRequest reqStatus)
		{
			var request = new WorkRequest(cb, state,
										 propogateThreadPrincipal, propogateCallContext,
										 propogateHttpContext, propogateCASMarkers);
			reqStatus = request;
			return PostRequest(request);
		}

		#endregion

		#region ThreadPool.PostRequest(late bound)

		// Overloads for the late bound Delegate.DynamicInvoke-based targets.
		//
        /// <summary>
        /// Posts the request.
        /// </summary>
        /// <param name="cb">The cb.</param>
        /// <param name="args">The args.</param>
        /// <returns></returns>
		public bool PostRequest(Delegate cb, object[] args)
		{
			ISharedCacheWorkRequest notUsed;
			return PostRequest(cb, args, out notUsed);
		}

        /// <summary>
        /// Posts the request.
        /// </summary>
        /// <param name="cb">The cb.</param>
        /// <param name="args">The args.</param>
        /// <param name="reqStatus">The req status.</param>
        /// <returns></returns>
		public bool PostRequest(Delegate cb, object[] args, out ISharedCacheWorkRequest reqStatus)
		{
			var request = new WorkRequest(cb, args,
										 propogateThreadPrincipal, propogateCallContext,
										 propogateHttpContext, propogateCASMarkers);
			reqStatus = request;
			return PostRequest(request);
		}

		#endregion

		/// <summary>
        /// The actual implementation of PostRequest.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns></returns>
		bool PostRequest(WorkRequest request)
		{
			lock (this)
			{
				// A requestQueueLimit of -1 means the queue is "unbounded"
				// (subject to available resources).  IOW, no artificial limit
				// has been placed on the maximum # of requests that can be
				// placed into the queue.
				//
				if ((requestQueueLimit == -1) || (requestQueue.Count < requestQueueLimit))
				{
					try
					{
						requestQueue.Enqueue(request);
						Monitor.Pulse(this);
						return (true);
					}
					catch
					{
					}

				}
			}

			return (false);
		}

		void ResetWorkRequestTimes()
		{
			lock (this)
			{
				DateTime newTime = DateTime.Now; // DateTime.Now.Add(pool.newThreadTrigger);

				foreach (WorkRequest wr in requestQueue)
				{
					wr.workingTime = newTime;
				}
			}
		}

		#region Private ThreadPool constants

		// Default parameters.
		//
		const int DEFAULT_DYNAMIC_THREAD_DECAY_TIME = 5 /* minutes */ * 60 /* sec/min */ * 1000 /* ms/sec */;
		const int DEFAULT_NEW_THREAD_TRIGGER_TIME = 500; // milliseconds
		const ThreadPriority DEFAULT_THREAD_PRIORITY = ThreadPriority.Normal;
		const int DEFAULT_REQUEST_QUEUE_LIMIT = -1; // unbounded

		#endregion

		#region Private ThreadPool member variables

		private bool hasBeenStarted;
		private bool stopInProgress;
		private readonly string threadPoolName;
		private readonly int initialThreadCount;     // Initial # of threads to create (called "static threads" in this class).
		private int maxThreadCount;         // Cap for thread count.  Threads added above initialThreadCount are called "dynamic" threads.
		private int currentThreadCount; // Current # of threads in the pool (static + dynamic).
		private int decayTime;              // If a dynamic thread is idle for this period of time w/o processing work requests, it will exit.
		private TimeSpan newThreadTrigger;       // If a work request sits in the queue this long before being processed, a new thread will be added to queue up to the max.
		private ThreadPriority threadPriority;
		private readonly ManualResetEvent stopCompleteEvent = new ManualResetEvent(false); // Signaled after Stop called and last thread exits.
		private readonly Queue requestQueue;
		private int requestQueueLimit;      // Throttle for maximum # of work requests that can be added.
		private bool useBackgroundThreads = true;
		private bool propogateThreadPrincipal;
		private bool propogateCallContext;
		private bool propogateHttpContext;
		private bool propogateCASMarkers;

		#endregion

		#region ThreadPool.ThreadInfo

		class ThreadInfo
		{
			public static ThreadInfo Capture(bool propogateThreadPrincipal, bool propogateCallContext, bool propogateHttpContext, bool propogateCASMarkers)
			{
				return new ThreadInfo(propogateThreadPrincipal, propogateCallContext, propogateHttpContext, propogateCASMarkers);
			}

			public static ThreadInfo Impersonate(ThreadInfo ti)
			{
				if (ti == null) throw new ArgumentNullException("ti");

				ThreadInfo prevInfo = Capture(true, true, true, true);
				Restore(ti);
				return (prevInfo);
			}

			public static void Restore(ThreadInfo ti)
			{
				if (ti == null) throw new ArgumentNullException("ti");

				// Restore call context.
				//
				if (miSetLogicalCallContext != null)
				{
					miSetLogicalCallContext.Invoke(Thread.CurrentThread, new object[] { ti.callContext });
				}

				// Restore HttpContext with the moral equivalent of
				// HttpContext.Current = ti.httpContext;
				//
				CallContext.SetData(HttpContextSlotName, ti.httpContext);

				// Restore thread identity.  It's important that this be done after
				// restoring call context above, since restoring call context also
				// overwrites the current thread principal setting.  If propogateCallContext
				// and propogateThreadPrincipal are both true, then the following is redundant.
				// However, since propogating call context requires the use of reflection
				// to capture/restore call context, I want that behavior to be independantly
				// switchable so that it can be disabled; while still allowing thread principal
				// to be propogated.  This also covers us in the event that call context
				// propogation changes so that it no longer propogates thread principal.
				//
				Thread.CurrentPrincipal = ti.principal;

                //if (ti.compressedStack != null)
                //{
					// TODO: Uncomment the following when Thread.SetCompressedStack is no longer guarded
					//       by a StrongNameIdentityPermission.
					//
					// Thread.CurrentThread.SetCompressedStack(ti.compressedStack);
                //}
			}

			private ThreadInfo(bool propogateThreadPrincipal, bool propogateCallContext,
													bool propogateHttpContext, bool propogateCASMarkers)
			{
				if (propogateThreadPrincipal)
				{
					principal = Thread.CurrentPrincipal;
				}

				if (propogateHttpContext)
				{
					httpContext = HttpContext.Current;
				}

				if (propogateCallContext && (miGetLogicalCallContext != null))
				{
					callContext = (LogicalCallContext)miGetLogicalCallContext.Invoke(Thread.CurrentThread, null);
					callContext = (LogicalCallContext)callContext.Clone();

					// TODO: consider serialize/deserialize call context to get a MBV snapshot
					//       instead of leaving it up to the Clone method.
				}

				if (propogateCASMarkers)
				{
					// TODO: Uncomment the following when Thread.GetCompressedStack is no longer guarded
					//       by a StrongNameIdentityPermission.
					//
					// compressedStack = Thread.CurrentThread.GetCompressedStack();
				}
			}

		    readonly IPrincipal principal;
		    readonly LogicalCallContext callContext;
            //CompressedStack compressedStack; // Always null until Get/SetCompressedStack are opened up.
		    readonly HttpContext httpContext;

			// Cached type information.
			//
			const BindingFlags bfNonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
			const BindingFlags bfNonPublicStatic = BindingFlags.Static | BindingFlags.NonPublic;

			static readonly MethodInfo miGetLogicalCallContext = typeof(Thread).GetMethod("GetLogicalCallContext", bfNonPublicInstance);

			static readonly MethodInfo miSetLogicalCallContext = typeof(Thread).GetMethod("SetLogicalCallContext", bfNonPublicInstance);

			static readonly string HttpContextSlotName;

			static ThreadInfo()
			{
				// Lookup the value of HttpContext.CallContextSlotName (if it exists)
				// to see what the name of the call context slot is where HttpContext.Current
				// is stashed.  As a fallback, if this field isn't present anymore, just
				// try for the original "HttpContext" slot name.
				//
				FieldInfo fi = typeof(HttpContext).GetField("CallContextSlotName", bfNonPublicStatic);

				if (fi != null)
				{
					HttpContextSlotName = (string)fi.GetValue(null);
				}
				else
				{
					HttpContextSlotName = "HttpContext";
				}
			}
		}

		#endregion

		#region ThreadPool.WorkRequest

		class WorkRequest : ISharedCacheWorkRequest
		{
			internal const int PENDING = 0;
			internal const int PROCESSED = 1;
			internal const int CANCELLED = 2;

			public WorkRequest(SharedCacheWorkRequestDelegate cb, object arg,
													bool propogateThreadPrincipal, bool propogateCallContext,
													bool propogateHttpContext, bool propogateCASMarkers)
			{
				targetProc = cb;
				procArg = arg;
				procArgs = null;

				Initialize(propogateThreadPrincipal, propogateCallContext,
										propogateHttpContext, propogateCASMarkers);
			}

			public WorkRequest(Delegate cb, object[] args,
													bool propogateThreadPrincipal, bool propogateCallContext,
													bool propogateHttpContext, bool propogateCASMarkers)
			{
				targetProc = cb;
				procArg = null;
				procArgs = args;

				Initialize(propogateThreadPrincipal, propogateCallContext,
										propogateHttpContext, propogateCASMarkers);
			}

			void Initialize(bool propogateThreadPrincipal, bool propogateCallContext,
											 bool propogateHttpContext, bool propogateCASMarkers)
			{
				workingTime = timeStampStarted = DateTime.Now;
				threadInfo = ThreadInfo.Capture(propogateThreadPrincipal, propogateCallContext,
																				 propogateHttpContext, propogateCASMarkers);
			}

			public bool Cancel()
			{
				// If the work request was pending, mark it cancelled.  Otherwise,
				// this method was called too late.  Note that this call can
				// cancel an operation without any race conditions.  But if the
				// result of this test-and-set indicates the request is in the
				// "processed" state, it might actually be about to be processed.
				//
				return (Interlocked.CompareExchange(ref state, CANCELLED, PENDING) == PENDING);
			}

			internal Delegate targetProc;         // Function to call.
			internal object procArg;            // State to pass to function.
			internal object[] procArgs;           // Used with Delegate.DynamicInvoke.
			internal DateTime timeStampStarted;   // Time work request was originally enqueued (held constant).
			internal DateTime workingTime;        // Current timestamp used for triggering new threads (moving target).
			internal ThreadInfo threadInfo;         // Everything we know about a thread.
			internal int state = PENDING;    // The state of this particular request.
		}

		#endregion

		#region ThreadPool.ThreadWrapper

		class ThreadWrapper
		{
		    readonly SharedCacheThreadPool pool;
		    readonly bool isPermanent;
		    readonly ThreadPriority priority;
		    readonly string name;

			public ThreadWrapper(SharedCacheThreadPool pool, bool isPermanent, ThreadPriority priority, string name)
			{
				this.pool = pool;
				this.isPermanent = isPermanent;
				this.priority = priority;
				this.name = name;

				lock (pool)
				{
					// Update the total # of threads in the pool.
					//
					pool.currentThreadCount++;
				}
			}

			public void Start()
			{
				var t = new Thread(ThreadProc);
				t.SetApartmentState(ApartmentState.MTA);
				t.Name = name;
				t.Priority = priority;
				t.IsBackground = pool.useBackgroundThreads;
				t.Start();
			}

			void ThreadProc()
			{
                Debug.WriteLine(string.Format("[{0}, {1}] Worker thread started", Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name));

				bool done = false;

				while (!done)
				{
					WorkRequest wr = null;
					ThreadWrapper newThread = null;

					lock (pool)
					{
						// As long as the request queue is empty and a shutdown hasn't
						// been initiated, wait for a new work request to arrive.
						//
						bool timedOut = false;

						while (!pool.stopInProgress && !timedOut && (pool.requestQueue.Count == 0))
						{
							if (!Monitor.Wait(pool, (isPermanent ? Timeout.Infinite : pool.decayTime)))
							{
								// Timed out waiting for something to do.  Only dynamically created
								// threads will get here, so bail out.
								//
								timedOut = true;
							}
						}

						// We exited the loop above because one of the following conditions
						// was met:
						//   - ThreadPool.Stop was called to initiate a shutdown.
						//   - A dynamic thread timed out waiting for a work request to arrive.
						//   - There are items in the work queue to process.

						// If we exited the loop because there's work to be done,
						// a shutdown hasn't been initiated, and we aren't a dynamic thread
						// that timed out, pull the request off the queue and prepare to
						// process it.
						//
						if (!pool.stopInProgress && !timedOut && (pool.requestQueue.Count > 0))
						{
							wr = (WorkRequest)pool.requestQueue.Dequeue();
							Debug.Assert(wr != null);

							// Check to see if this work request languished in the queue
							// very long.  If it was in the queue >= the new thread trigger
							// time, and if we haven't reached the max thread count cap,
							// add a new thread to the pool.
							//
							// If the decision is made, create the new thread object (updating
							// the current # of threads in the pool), but defer starting the new
							// thread until the lock is released.
							//
							TimeSpan requestTimeInQ = DateTime.Now.Subtract(wr.workingTime);

							if ((requestTimeInQ >= pool.newThreadTrigger) && (pool.currentThreadCount < pool.maxThreadCount))
							{
								// Note - the constructor for ThreadWrapper will update
								// pool.currentThreadCount.
								//
								newThread = new ThreadWrapper(pool, false, priority, string.Format("{0} (dynamic)", pool.threadPoolName));

								// Since the current request we just dequeued is stale,
								// everything else behind it in the queue is also stale.
								// So reset the timestamps of the remaining pending work
								// requests so that we don't start creating threads
								// for every subsequent request.
								//
								pool.ResetWorkRequestTimes();
							}
						}
						else
						{
							// Should only get here if this is a dynamic thread that
							// timed out waiting for a work request, or if the pool
							// is shutting down.
							//
							Debug.Assert((timedOut && !isPermanent) || pool.stopInProgress);
							pool.currentThreadCount--;

							if (pool.currentThreadCount == 0)
							{
								// Last one out turns off the lights.
								//
								Debug.Assert(pool.stopInProgress);

								if (pool.Stopped != null)
								{
									pool.Stopped();
								}

								pool.stopCompleteEvent.Set();
							}

							done = true;
						}
					} // lock

					// No longer holding pool lock here...

					if (!done)
					{
						// Check to see if this request has been cancelled while
						// stuck in the work queue.
						//
						// If the work request was pending, mark it processed and proceed
						// to handle.  Otherwise, the request must have been cancelled
						// before we plucked it off the request queue.
						//
						if (Interlocked.CompareExchange(ref wr.state, WorkRequest.PROCESSED, WorkRequest.PENDING) != WorkRequest.PENDING)
						{
							// Request was cancelled before we could get here.
							// Bail out.
							continue;
						}

						if (newThread != null)
						{
                            Debug.WriteLine(string.Format("[{0}, {1}] Adding dynamic thread to pool", Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name));
							newThread.Start();
						}

						// Dispatch the work request.
						//
						ThreadInfo originalThreadInfo = null;

						try
						{
							// Impersonate (as much as possible) what we know about
							// the thread that issued the work request.
							//
							originalThreadInfo = ThreadInfo.Impersonate(wr.threadInfo);

							var targetProc = wr.targetProc as SharedCacheWorkRequestDelegate;

							if (targetProc != null)
							{
								targetProc(wr.procArg, wr.timeStampStarted);
							}
							else
							{
								wr.targetProc.DynamicInvoke(wr.procArgs);
							}
						}
						catch (Exception e)
						{
							Debug.WriteLine(string.Format("Exception thrown performing callback:\n{0}\n{1}", e.Message, e.StackTrace));
						}
						finally
						{
							// Restore our worker thread's identity.
							//
							ThreadInfo.Restore(originalThreadInfo);
						}
					}
				}

                Debug.WriteLine(string.Format("[{0}, {1}] Worker thread exiting pool", Thread.CurrentThread.ManagedThreadId, Thread.CurrentThread.Name));
			}
		}

		#endregion
	}
}
