using System;
using System.Threading.Tasks;

namespace NuGet.PackageManagement.UI
{
    public class BackgroundLoader<T>
    {
        private bool _loaderHasBeenRun;
        private Lazy<Task<T>> _loaderTask;

        public BackgroundLoader(Lazy<Task<T>> loaderTask)
        {
            _loaderHasBeenRun = false;
            _loaderTask = loaderTask;
        }

        public bool LoaderHasBeenRun
        {
            get
            {
                return _loaderHasBeenRun;
            }
            set
            {
                _loaderHasBeenRun = true;
            }
        }

        public Task<T> GetResult()
        {
            return _loaderTask.Value;
        }
    }
}