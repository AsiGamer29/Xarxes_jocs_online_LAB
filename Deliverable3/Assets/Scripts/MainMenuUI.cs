using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    void Start()
    {
        Application.runInBackground = true;
        ServerSession.Reset();        
    }

    public void OnHostClicked()
    {
        SceneManager.LoadScene(ServerSession.SceneCreate);
    }

    public void OnJoinClicked()
    {
        SceneManager.LoadScene(ServerSession.SceneJoin);
    }
}