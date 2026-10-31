// VI: Giới hạn số fixture tạo schema đồng thời trên SQL container local/CI. EN: Bound concurrent schema creation to the capacity of the local and CI SQL container.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
