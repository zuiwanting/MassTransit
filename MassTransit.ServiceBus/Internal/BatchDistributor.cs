namespace MassTransit.ServiceBus.Internal
{
	using System;
	using System.Collections.Generic;

	/// <summary>
	/// A batch distributor is subscribed to a message type so that it can dispatch batches of messages
	/// to new consumers for each batch
	/// </summary>
	/// <typeparam name="TMessage">The type of message that is being batched</typeparam>
	/// <typeparam name="TBatchId">The type for the batch id</typeparam>
	public class BatchDistributor<TMessage, TBatchId> :
		Consumes<TMessage>.Selected
		where TMessage : class, BatchedBy<TBatchId>
	{
		private readonly Dictionary<TBatchId, Batch<TMessage, TBatchId>> _batches = new Dictionary<TBatchId, Batch<TMessage, TBatchId>>();
		private readonly Consumes<Batch<TMessage, TBatchId>>.Selected _consumer;

		private readonly object _lockContext = new object();

		private readonly TimeSpan _timeout;

		public BatchDistributor(Consumes<Batch<TMessage, TBatchId>>.Selected consumer)
		{
			_consumer = consumer;
			_timeout = TimeSpan.FromMinutes(30);
		}

		public BatchDistributor(Consumes<Batch<TMessage, TBatchId>>.Selected consumer, TimeSpan timeout)
		{
			_consumer = consumer;
			_timeout = timeout;
		}

		public bool Accept(TMessage message)
		{
			lock (_lockContext)
				if (_batches.ContainsKey(message.BatchId))
					return true;

			Batch<TMessage, TBatchId> batch = new Batch<TMessage, TBatchId>(message.BatchLength, message.BatchId, _timeout);

			if (_consumer.Accept(batch))
				return true;

			return false;
		}

		public void Consume(TMessage message)
		{
			TBatchId batchId = message.BatchId;

			Batch<TMessage, TBatchId> batch;

			bool invokeHandler = false;

			lock (_lockContext)
			{
				if (!_batches.ContainsKey(batchId))
				{
					batch = new Batch<TMessage, TBatchId>(message.BatchLength, batchId, _timeout);

					_batches.Add(batchId, batch);

					invokeHandler = true;
				}
				else
				{
					batch = _batches[batchId];
				}
			}

			// push this message to the context, releasing the enumerator
			batch.Consume(message);

			if (invokeHandler)
			{
				_consumer.Consume(batch);
			}
		}
	}
}