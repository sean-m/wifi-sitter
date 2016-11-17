using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using WifiSitter;
using WifiSitter.Model;

namespace WifiSitterGui.ViewModel
{
    public class MainWindowViewModel : MvvmObservable
    {
        #region fields

        SimpleNetworkState _netState;

        #endregion  // fields


        #region constructor

        public MainWindowViewModel () {
            _netState = new SimpleNetworkState();
        }

        #endregion  // constructor


        #region properties
        
        public SimpleNetworkState NetState {
            get { return _netState; }
            set { _netState = value; OnPropertyChanged(null); }
        }
        
        public List<SimpleNic> Nics { get { return NetState.Nics; } }

        #endregion  // properties


        #region methods
        #endregion  // methods


        #region eventhandlers
        #endregion  // methods
    }
}
