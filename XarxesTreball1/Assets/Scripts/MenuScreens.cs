using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuScreens : MonoBehaviour
{

    string m_name, m_ip, m_port, m_localIp;
    string m_error = "";
    bool m_busy;       
    bool m_done;    
    
    void Start()
    {
        Application.runInBackground = true;

    }

   

    void Update()
    {
        if (!m_busy || m_done) return;


    }


    void CreateGame()
    {



        m_busy = true;
    }

    void JoinGame()
    {



        m_busy = true;
    }

    void CancelJoin()
    {

    }

}
