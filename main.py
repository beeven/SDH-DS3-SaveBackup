from genericpath import isfile
import logging
from multiprocessing.managers import ListProxy
import os, os.path
import shutil
import json
import datetime
import sys
import subprocess
import multiprocessing


py_modules_folder = os.path.dirname(os.path.realpath(__file__))+"/py_modules"
sys.path.append(py_modules_folder)
print(sys.path)

import zmq
#import importlib.util
#spec = importlib.util.spec_from_file_location("msal",py_modules_folder+"/msal/__init__.py")
#msal = importlib.util.module_from_spec(spec)
#msal = importlib.util.spec_from_file_location("msal",py_modules_folder+"/msal/__init__.py")

logging.basicConfig(filename="/tmp/sdh-ds3-savebackup.log",
                    format='%(asctime)s %(levelname)s %(message)s',
                    filemode='w+',
                    force=True)
logger=logging.getLogger()
logger.setLevel(logging.INFO) # can be changed to logging.DEBUG for debugging issues

CLIENT_ID = "7e9bf271-a6cd-4786-b4f6-7980ff10acf8"
WORKING_DIR = "/home/deck/.local/share/SDH-DS3-SaveBackup"
SCOPES = ["user.read","Files.ReadWrite"]
CACHE_FILE_NAME = "msal_token_cache.json"
SAVE_FILE_PATH = "/home/deck/.local/share/Steam/steamapps/compatdata/374320/pfx/drive_c/users/steamuser/AppData/Roaming/DarkSoulsIII/0110000100f9e486/"
SAVE_FILE_NAME = "DS30000.sl2"
CONFIG_FILE_NAME = "ds3-savebackup.json"
MAX_SLOTS = 3

INITIALIZED = False

SLOT_CONFIG = {}
plugin_proc: subprocess.Popen = None
manager = multiprocessing.Manager()
status_list = manager.list([])
update_status_process: multiprocessing.Process = None

def _load_config():
    global SLOT_CONFIG
    try:
        with open(SAVE_FILE_PATH + CONFIG_FILE_NAME, encoding="utf-8") as f:
            SLOT_CONFIG = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        SLOT_CONFIG = { 
            "selectedSlot": 1,
            "slots": [] }
        for i in range(MAX_SLOTS):
            SLOT_CONFIG["slots"].append({"lastModified": None})
            
def _save_config():
    with open(SAVE_FILE_PATH + CONFIG_FILE_NAME, "w", encoding="utf-8") as f:
        json.dump(SLOT_CONFIG, f, ensure_ascii=False)


def invoke_plugin(command:str, status_lines:ListProxy):
    ctx = zmq.Context()
    zsock = ctx.socket(zmq.PULL)
    zport = zsock.bind_to_random_port("tcp://127.0.0.1")
    plugin_proc = subprocess.Popen([os.path.dirname(os.path.realpath(__file__))+"/bin/ds3-savebackup-onedrive","--port",str(zport),command])
    del status_lines[:]

    while plugin_proc.poll() is None:
        msg = zsock.recv_string()
        status_lines.append(msg)
        if msg == "done":
            break
    zsock.close()
    ctx.destroy()
    plugin_proc.wait()
    outs,errs = plugin_proc.communicate()
    print(outs)
    print("plugin exited.")
    

class Plugin:

    async def select_slot(self, slot):
        if slot is None:
            slot = 1
        SLOT_CONFIG["selectedSlot"] = slot
        _save_config()
        logger.info("select slot: %d", slot)
        return slot
    
    async def get_slot_config(self):
        logger.info("get slot config: %s", json.dumps(SLOT_CONFIG))
        _load_config()
        return SLOT_CONFIG

    async def save_load(self, is_save):
        if len(SLOT_CONFIG) == 0:
            _load_config()
        slot = SLOT_CONFIG["selectedSlot"]
        backup = SAVE_FILE_NAME+"."+str(slot)+".bak"
        if is_save:
            shutil.copy(SAVE_FILE_PATH+SAVE_FILE_NAME, SAVE_FILE_PATH+backup)
            logger.info("Save %d backup completed.", slot)
            SLOT_CONFIG["selectedSlot"] = slot
            SLOT_CONFIG["slots"][slot-1]["lastModified"] = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
            _save_config()
            return "Backup completed."
        else:
            if os.path.isfile(SAVE_FILE_PATH+backup):
                shutil.copy(SAVE_FILE_PATH+backup, SAVE_FILE_PATH+SAVE_FILE_NAME)
                logger.info("Save %d recovered.", slot)
                return "Restore completed."
            else:
                logger.info("Save %d is empty.", slot)
                return f"{slot} is empty."



    async def signin(self):
        logger.info("Sign in called")
        global plugin_proc
        if plugin_proc is not None and plugin_proc.poll() is None:
            plugin_proc.kill()

        ctx = zmq.Context()
        zsock = ctx.socket(zmq.REP)
        zport = zsock.bind_to_random_port("tcp://127.0.0.1")
        plugin_proc = subprocess.Popen([os.path.dirname(os.path.realpath(__file__))+"/bin/ds3-savebackup-onedrive","--port",str(zport),"login"], stdout=subprocess.PIPE)
        auth_info = zsock.recv_json()
        logger.info(auth_info)
        zsock.send_string("")
        zsock.close()
        ctx.destroy()
        if not auth_info["Ok"]:
            return auth_info["LoginInfo"]["VerificationUrl"]
        else:
            return ""

    async def upload(self):
        global update_status_process
        logger.info("Upload called.")
        if update_status_process is not None:
            if update_status_process.is_alive():
                update_status_process.terminate()
        update_status_process = multiprocessing.Process(target=invoke_plugin, args=("upload",status_list))
        update_status_process.start()
        return update_status_process.pid

    async def download(self):
        global plugin_proc, status
        logger.info("Download called.")
        if update_status_process is not None:
            if update_status_process.is_alive():
                update_status_process.terminate()
        update_status_process = multiprocessing.Process(target=invoke_plugin, args=("download",status_list))
        update_status_process.start()
        return update_status_process.pid

    async def get_status(self):
        while len(status_list) > 30:
            status_list.pop(0)
        return "\n".join(status_list)
        

    async def check_login_status(self):
        if plugin_proc is not None and plugin_proc.poll() is None:
            return """{"status":"running"}"""
        else:
            return """{"status":"stopped", "retcode":""}"""


    # Asyncio-compatible long-running code, executed in a task when the plugin is loaded
    async def _main(self):
        logger.info("Dark Souls 3 Save Backup Plugin is running.")
        _load_config()

    
    async def _unload(self):
        #global zcontext
        logger.info("DS3 Savebackup is unloading.")