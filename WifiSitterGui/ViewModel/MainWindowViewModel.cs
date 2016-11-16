using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using WifiSitter;

namespace WifiSitterGui.ViewModel
{
    class MainWindowViewModel : MvvmObservable
    {
        #region fields

        WifiSitter.NetworkState _netState;

        #endregion  // fields


        #region constructor

        public MainWindowViewModel () {
            _netState = new NetworkState(new string[] { });
        }

        #endregion  // constructor


        #region properties
        
        public NetworkState NetState {
            get { return _netState; }
            set { _netState = value; }
        }
        
        #endregion  // properties


        #region methods
        #endregion  // methods


        #region eventhandlers
        #endregion  // methods
    }
}
