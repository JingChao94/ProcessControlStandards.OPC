﻿#region using

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Permissions;

using ProcessControlStandards.OPC.Core;

#endregion

namespace ProcessControlStandards.OPC.DataAccessClient
{
    /// <summary>
    /// OPC DA group. Helps to access to item values.
    /// </summary>
	public class Group : IDisposable
	{
		#region DataCallback

		private class DataCallback : IOPCDataCallback
		{
			public DataCallback(Group @group)
			{
				this.@group = @group;
			}

			public void OnDataChange(int transactionId, int groupId, int quality, int error, uint count, int[] clientIds, IntPtr values, short[] qualities, long[] timeStamps, int[] errors)
			{
				if (@group.ClientId != groupId)
					return;

				var handler = @group.dataChangeHandlers;
				if(handler != null)
					handler(@group, new DataChangeEventArgs(
                        groupId, 
                        transactionId, 
                        quality, 
                        error,
                        ItemValueReader.Read(clientIds, values, qualities, timeStamps, errors)));
			}

			public void OnReadComplete(int transactionId, int groupId, int quality, int error, uint count, int[] clientIds, IntPtr values, short[] qualities, long[] timeStamps, int[] errors)
			{
				if (@group.ClientId != groupId)
					return;

				var handler = @group.readCompleteHandlers;
				if(handler != null)
					handler(@group, new DataChangeEventArgs(
                        groupId, 
                        transactionId, 
                        quality, 
                        error, 
                        ItemValueReader.Read(clientIds, values, qualities, timeStamps, errors)));
			}

			public void OnWriteComplete(int transactionId, int groupId, int error, uint count, int[] clientIds, int[] errors)
			{
				if (@group.ClientId != groupId)
					return;

				var handler = @group.writeCompleteHandlers;
				if (handler != null)
				{
					var results = new KeyValuePair<int, int>[count];
					for (var i = 0; i < count; i++)
						results[i] = new KeyValuePair<int, int>(clientIds[i], errors[i]);
					
					handler(@group, new WriteCompleteEventArgs(groupId, transactionId, error, results));
				}
			}

			public void OnCancelComplete(int transactionId, int groupId)
			{
				if (@group.ClientId != groupId)
					return;

				var handler = @group.cancelCompleteHandlers;
				if (handler != null)
					handler(@group, new CompleteEventArgs(groupId, transactionId));
			}

			private readonly Group @group;
		}

		#endregion

		internal Group(DAServer server, int clientId, int serverId, string name, int updateRate, IOPCItemMgt @group)
		{
			this.server = server;
			ClientId = clientId;
			ServerId = serverId;
			Name = name;
			this.@group = @group;
			UpdateRate = updateRate;

			syncIO = (IOPCSyncIO) @group;
			groupManagement = (IOPCGroupStateMgt) @group;
			try
			{
				asyncIO = (IOPCAsyncIO2) @group;				
			}
			catch (InvalidCastException)
			{				
			}
			try
			{
				connectionPointContainer = (IConnectionPointContainer) @group;				
			}
			catch (InvalidCastException)
			{				
			}
		}

		[SecurityPermission(SecurityAction.LinkDemand)] 
		~Group()
		{
			Dispose(false);
		}

        /// <summary>
        /// Item values is changed.
        /// </summary>
		public event EventHandler<DataChangeEventArgs> DataChange
		{
			add
			{
				InitializeAsyncMode();

				dataChangeHandlers += value;
			}
			remove
			{
				// ReSharper disable DelegateSubtraction
				dataChangeHandlers -= value;
				// ReSharper restore DelegateSubtraction
			}
		}

        /// <summary>
        /// Reading data is completed.
        /// </summary>
		public event EventHandler<DataChangeEventArgs> ReadComplete
		{
			add
			{
				InitializeAsyncMode();

				readCompleteHandlers += value;
			}
			remove
			{
				// ReSharper disable DelegateSubtraction
				readCompleteHandlers -= value;
				// ReSharper restore DelegateSubtraction
			}
		}

        /// <summary>
        /// Writing data is completed.
        /// </summary>
		public event EventHandler<WriteCompleteEventArgs> WriteComplete
		{
			add
			{
				InitializeAsyncMode();

				writeCompleteHandlers += value;
			}
			remove
			{
				// ReSharper disable DelegateSubtraction
				writeCompleteHandlers -= value;
				// ReSharper restore DelegateSubtraction
			}
		}

        /// <summary>
        /// Cancellation is completed.
        /// </summary>
		public event EventHandler<CompleteEventArgs> CancelComplete
		{
			add
			{
				InitializeAsyncMode();

				cancelCompleteHandlers += value;
			}
			remove
			{
				// ReSharper disable DelegateSubtraction
				cancelCompleteHandlers -= value;
				// ReSharper restore DelegateSubtraction
			}
		}

        /// <summary>
        /// OPC DA group client ID.
        /// </summary>
		public int ClientId { get; private set; }

        /// <summary>
        /// OPC DA group server ID.
        /// </summary>
		public int ServerId { get; private set; }

        /// <summary>
        /// Name of OPC DA group.
        /// </summary>
		public string Name { get; private set; }

        /// <summary>
        /// Item values update rate.
        /// </summary>
		public int UpdateRate { get; private set; }

        /// <summary>
        /// true - OPC DA server supports asynchronous reading.
        /// </summary>
        public bool IsAsyncIOSupported
        {
            get { return asyncIO != null && connectionPointContainer != null; }
        }

        /// <summary>
        /// Retrieves OPC DA group properties.
        /// </summary>
        /// <returns>OPC DA group properties.</returns>
		public GroupProperties GetProperties()
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");

			string name;
			float percentDeadband;
			int updateRate, activeAsInt, timeBias, locale, clientId, serverId;

			groupManagement.GetState(
				out updateRate, 
				out activeAsInt, 
				out name, 
				out timeBias, 
				out percentDeadband, 
				out locale, 
				out clientId, 
				out serverId);

			return new GroupProperties
			{
				UpdateRate = updateRate,
				Active = activeAsInt != 0,
				Name = name,
				TimeBias = timeBias,
				PercentDeadband = percentDeadband,
				Locale = new CultureInfo(locale),
				ClientId = clientId,
				ServerId = serverId,
			};
		}

        /// <summary>
        /// Sets new OPC DA group properties.
        /// </summary>
        /// <param name="properties">New OPC DA group properties.</param>
		public void SetProperties(GroupProperties properties)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");

			int revisedUpdateRate;
			groupManagement.SetState(
				properties.UpdateRate, 
				out revisedUpdateRate, 
				properties.Active ? 1 : 0, 
				properties.TimeBias, 
				properties.PercentDeadband, 
				properties.Locale.LCID, 
				properties.ClientId);

			ClientId = properties.ClientId;
			UpdateRate = revisedUpdateRate;

			if(!string.Equals(properties.Name, Name))
				groupManagement.SetName(properties.Name);
			Name = properties.Name;
		}

        /// <summary>
        /// Adds items to OPC DA group to read/write their values.
        /// </summary>
        /// <param name="items">Items to add.</param>
        /// <returns>Result of each item adding .</returns>
		[SecurityPermission(SecurityAction.LinkDemand)] 
		public ItemResult[] AddItems(Item[] items)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			items.ArgumentNotNull("items");
			if(items.Length == 0)
				return new ItemResult[0];

			using(var reader = new ItemResultReader(items))
			{
				IntPtr dataPtr;
			    IntPtr errorsPtr;
                @group.AddItems((uint)items.Length, reader.Items, out dataPtr, out errorsPtr);
                return reader.Read(dataPtr, errorsPtr);
			}				
		}

        /// <summary>
        /// Tests OPC DA group items before adding.
        /// </summary>
        /// <param name="items">Items to test.</param>
        /// <returns>>Result of each item testing .</returns>
		[SecurityPermission(SecurityAction.LinkDemand)] 
		public ItemResult[] ValidateItems(Item[] items)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			items.ArgumentNotNull("items");
			if(items.Length == 0)
				return new ItemResult[0];

			using(var reader = new ItemResultReader(items))
			{
				IntPtr dataPtr;
                IntPtr errorsPtr;
                @group.ValidateItems((uint)items.Length, reader.Items, 0, out dataPtr, out errorsPtr);
                return reader.Read(dataPtr, errorsPtr);
			}				
		}

        /// <summary>
        /// Removes OPC DA group items to stop reading/writing their values.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <returns>Result of each item removing.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
		public int[] RemoveItems(int[] serverIds)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			serverIds.ArgumentNotNull("serverIds");
			if(serverIds.Length == 0)
				return new int[0];

            IntPtr errorsPtr;
            @group.RemoveItems((uint)serverIds.Length, serverIds, out errorsPtr);

            return ItemResultReader.Read(serverIds.Length, errorsPtr);
		}

        /// <summary>
        /// Changes OPC Da group item state.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <param name="active">true - active state.</param>
        /// <returns>Result of each item changing.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
        public int[] SetActiveState(int[] serverIds, bool active)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			serverIds.ArgumentNotNull("serverIds");
			if(serverIds.Length == 0)
				return new int[0];

            IntPtr errorsPtr;
            @group.SetActiveState((uint)serverIds.Length, serverIds, active ? 1 : 0, out errorsPtr);

            return ItemResultReader.Read(serverIds.Length, errorsPtr);
		}
        
        /// <summary>
        /// Sets client ID for each OPC DA group item.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <param name="clientIds">New client ID of items.</param>
        /// <returns>Result of each item changing.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
        public int[] SetClientHandles(int[] serverIds, int[] clientIds)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			serverIds.ArgumentNotNull("serverIds");
			if(serverIds.Length == 0)
				return new int[0];

            IntPtr errorsPtr;
            @group.SetClientHandles((uint)serverIds.Length, serverIds, clientIds, out errorsPtr);

            return ItemResultReader.Read(serverIds.Length, errorsPtr);
		}

        /// <summary>
        /// Sets data type for each OPC DA group item.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <param name="types">New data type of items.</param>
        /// <returns>Result of each item changing.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
        public int[] SetDatatypes(int[] serverIds, VarEnum[] types)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			serverIds.ArgumentNotNull("serverIds");
			if(serverIds.Length == 0)
				return new int[0];

			var typesAsShort = new short[types.Length];
			for (var i = 0; i < types.Length; i++)
				typesAsShort[i] = (short)types[i];

            IntPtr errorsPtr;
            @group.SetDatatypes((uint)serverIds.Length, serverIds, typesAsShort, out errorsPtr);

            return ItemResultReader.Read(serverIds.Length, errorsPtr);			
		}

        /// <summary>
        /// Reads OPC DA group item values synchronous.
        /// </summary>
        /// <param name="source">Read mode, see <see cref="DataSource"/>.</param>
        /// <param name="serverIds">Server ID of items.</param>
        /// <returns>OPC DA group item values.</returns>
		[SecurityPermission(SecurityAction.LinkDemand)] 
		public ItemValue[] SyncReadItems(DataSource source, int[] serverIds)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			serverIds.ArgumentNotNull("serverIds");
			if(serverIds.Length == 0)
				return new ItemValue[0];

			IntPtr dataPtr;
            IntPtr errorsPtr;
            syncIO.Read(source, (uint)serverIds.Length, serverIds, out dataPtr, out errorsPtr);

            return ItemValueReader.Read(serverIds.Length, dataPtr, errorsPtr);
		}

        /// <summary>
        /// Writes OPC DA group item values synchronous.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <param name="values">Item values.</param>
        /// <returns>>Result of each item writing.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
        public int[] SyncWriteItems(int[] serverIds, object[] values)
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			serverIds.ArgumentNotNull("serverIds");
			values.ArgumentNotNull("values");
			if(serverIds.Length == 0)
				return new int[0];

			using(var writer = new ItemValueWriter(values))
			{
                IntPtr errorsPtr;
                syncIO.Write((uint)serverIds.Length, serverIds, writer.Values, out errorsPtr);

                return ItemResultReader.Read(serverIds.Length, errorsPtr);
			}
		}

        /// <summary>
        /// Reads OPC DA group item values asynchronous.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <param name="transactionId">Transaction ID.</param>
        /// <param name="cancelId">Cancellation ID.</param>
        /// <returns>Result of starting of each item reading.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
        public int[] AsyncReadItems(int[] serverIds, int transactionId, out int cancelId)
        {
            cancelId = 0;
			if(@group == null)
				throw new ObjectDisposedException("Group");
			if(asyncIO == null)
				throw new NotSupportedException();
			serverIds.ArgumentNotNull("serverIds");
            if (serverIds.Length == 0)
                return new int[0];

            int tmp;
            IntPtr errorsPtr;
            asyncIO.Read((uint)serverIds.Length, serverIds, transactionId, out tmp, out errorsPtr);

			cancelId = tmp;
            return ItemResultReader.Read(serverIds.Length, errorsPtr);
        }

        /// <summary>
        /// Writes OPC DA group item values asynchronous.
        /// </summary>
        /// <param name="serverIds">Server ID of items.</param>
        /// <param name="values">Item values to write.</param>
        /// <param name="transactionId">Transaction ID.</param>
        /// <param name="cancelId">Cancellation ID.</param>
        /// <returns>Result of starting of each item writing.</returns>
        [SecurityPermission(SecurityAction.LinkDemand)] 
        public int[] AsyncWriteItems(int[] serverIds, object[] values, int transactionId, out int cancelId)
        {
            cancelId = 0;
			if(@group == null)
				throw new ObjectDisposedException("Group");
			if(asyncIO == null)
				throw new NotSupportedException();
			serverIds.ArgumentNotNull("serverIds");
            if (serverIds.Length == 0)
                return new int[0];

            using(var writer = new ItemValueWriter(values))
            {
                int tmp;
                IntPtr errorsPtr;
                asyncIO.Write((uint)serverIds.Length, serverIds, writer.Values, transactionId, out tmp, out errorsPtr);

                cancelId = tmp;
                return ItemResultReader.Read(serverIds.Length, errorsPtr);
            }
        }

        /// <summary>
        /// Starts refresh of item values reading in OPC DA group.
        /// </summary>
        /// <param name="source">Read mode, see <see cref="DataSource"/>.</param>
        /// <param name="transactionId">Transaction ID.</param>
        /// <param name="cancelId">Cancellation ID.</param>
        public void AsyncRefresh(DataSource source, int transactionId, out int cancelId)
        {
            cancelId = 0;
			if(@group == null)
				throw new ObjectDisposedException("Group");
			if(asyncIO == null)
				throw new NotSupportedException();

            int tmp;
            asyncIO.Refresh2(source, transactionId, out tmp);

            cancelId = tmp;
        }

        /// <summary>
        /// Cancels transaction.
        /// </summary>
        /// <param name="cancelId">Cancellation ID.</param>
        public void AsyncCancel(int cancelId)
        {
			if(@group == null)
				throw new ObjectDisposedException("Group");

            asyncIO.Cancel2(cancelId);
        }

        /// <summary>
        /// Enables asynchronous mode.
        /// </summary>
        /// <param name="enable">Asynchronous mode.</param>
        public void AsyncSetEnable(bool enable)
        {
			if(@group == null)
				throw new ObjectDisposedException("Group");
			if(asyncIO == null)
				throw new NotSupportedException();

            asyncIO.SetEnable(enable ? 1 : 0);	        
        }

        /// <summary>
        /// Retrieves asynchronous mode.
        /// </summary>
        /// <returns>Asynchronous mode.</returns>
        public bool AsyncGetEnable()
        {
			if(@group == null)
				throw new ObjectDisposedException("Group");
			if(asyncIO == null)
				throw new NotSupportedException();

	        int tmp;
            asyncIO.GetEnable(out tmp);

	        return tmp != 0;
        }

        /// <summary>
        /// Removes OPC DA group from server.
        /// </summary>
		[SecurityPermission(SecurityAction.LinkDemand)] 
		public void Dispose()
		{
			Dispose(true);

			GC.SuppressFinalize(this);
		}

        /// <summary>
        /// Removes OPC DA group from server.
        /// </summary>
        /// <param name="disposing">true - call in Dispose.</param>
        [SecurityPermission(SecurityAction.LinkDemand)] 
		protected virtual void Dispose(bool disposing)
		{
			if(@group != null)				
			{
				if (connectionPoint != null)
				{
					try
					{
						connectionPoint.Unadvise(asyncCookie);
					}
					finally
					{
						Marshal.ReleaseComObject(connectionPoint);
						connectionPoint = null;            	
					}
				}

				Marshal.ReleaseComObject(@group);

				if(disposing)
					server.RemoveGroup(ServerId);

				@group = null;
			}			
		}

		private void InitializeAsyncMode()
		{
			if(@group == null)
				throw new ObjectDisposedException("Group");
			if(asyncIO == null || connectionPointContainer == null)
				throw new NotSupportedException();

			if (asyncCallback != null)
				return;

            var dataCallbackId = new Guid("39c13a70-011e-11d0-9675-0020afd8adb3");
            IConnectionPoint point;
            connectionPointContainer.FindConnectionPoint(ref dataCallbackId, out point);
            if (point == null)
                throw new NotSupportedException();

			var callback = new DataCallback(this);
            point.Advise(callback, out asyncCookie);

			asyncCallback = callback;
            connectionPoint = point;
		}

		private readonly DAServer server;

		private IOPCItemMgt @group;

		private readonly IOPCGroupStateMgt groupManagement;

		private readonly IOPCSyncIO syncIO;

		private readonly IOPCAsyncIO2 asyncIO;

		private readonly IConnectionPointContainer connectionPointContainer;

		private DataCallback asyncCallback;

		private int asyncCookie;

		private IConnectionPoint connectionPoint;

		private EventHandler<DataChangeEventArgs> dataChangeHandlers;

		private EventHandler<DataChangeEventArgs> readCompleteHandlers;

		private EventHandler<WriteCompleteEventArgs> writeCompleteHandlers;

		private EventHandler<CompleteEventArgs> cancelCompleteHandlers;
	}
}
