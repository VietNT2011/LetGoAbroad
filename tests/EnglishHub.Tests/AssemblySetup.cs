// VI: Tạo schema theo từng collection để SQL local/CI không cạn pool bộ nhớ; các ca concurrency vẫn tự gửi request song song. EN: Serialize collection setup within the SQL memory budget; concurrency cases still issue parallel commands explicitly.
[assembly: CollectionBehavior(MaxParallelThreads = 1)]
