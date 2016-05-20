using System.ComponentModel;

namespace WifiSitterConfig.ViewModel
{
    public abstract class MvvmObservable : INotifyPropertyChanged, INotifyPropertyChanging
    {
        #region INotifyPropertyChanging Members

        public event PropertyChangingEventHandler PropertyChanging;

        internal virtual void OnPropertyChanging(string propertyName) {
            PropertyChangingEventHandler handler = this.PropertyChanging;
            if (handler != null)
                handler(this, new PropertyChangingEventArgs(propertyName));
        }

        #endregion // INotifyPropertyChanging Members


        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        internal virtual void OnPropertyChanged(string propertyName) {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion // INotifyPropertyChanged Members
    }
}
