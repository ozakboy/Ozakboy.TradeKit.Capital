// 這個測試組件不平行執行:工作階段測試會啟動假宿主行程,並以行程名稱清理殘留,平行跑會互相干擾。
// The test assembly does not run in parallel: the session tests start fake host processes and clean up leftovers by
// process name, so running in parallel would make them interfere.
[assembly: DoNotParallelize]
