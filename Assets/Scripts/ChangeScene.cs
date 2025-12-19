using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Y))
        {
            SceneManager.LoadScene("Template");
        }

        if (Input.GetKeyDown(KeyCode.U))
        {
            SceneManager.LoadScene("Chambre");
        }
        if (Input.GetKeyDown(KeyCode.I))
        {
            SceneManager.LoadScene("Chambre 2");
        }
    }
}