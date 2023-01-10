from genericpath import isfile
import logging
import os.path
import shutil
import json
import datetime
import sys

sys.path.append(os.path.dirname(os.path.realpath(__file__))+"/py_modules")

import pyzmq

logging.basicConfig(filename="/tmp/sdh-ds3-savebackup.log",
                    format='%(asctime)s %(levelname)s %(message)s',
                    filemode='w+',
                    force=True)
logger=logging.getLogger()
logger.setLevel(logging.INFO) # can be changed to logging.DEBUG for debugging issues

SAVE_FILE_PATH = "/home/deck/.local/share/Steam/steamapps/compatdata/374320/pfx/drive_c/users/steamuser/AppData/Roaming/DarkSoulsIII/0110000100f9e486/"
SAVE_FILE_NAME = "DS30000.sl2"
CONFIG_FILE_NAME = "ds3-savebackup.json"
MAX_SLOTS = 3

INITIALIZED = False

config = {}

def _load_config():
    global config
    try:
        with open(SAVE_FILE_PATH + CONFIG_FILE_NAME, encoding="utf-8") as f:
            config = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        config = { 
            "selectedSlot": 1,
            "slots": [] }
        for i in range(MAX_SLOTS):
            config["slots"].append({"lastModified": None})
            
def _save_config():
    with open(SAVE_FILE_PATH + CONFIG_FILE_NAME, "w", encoding="utf-8") as f:
        json.dump(config, f, ensure_ascii=False)


class Plugin:

    async def select_slot(self, slot):
        if slot is None:
            slot = 1
        config["selectedSlot"] = slot
        logger.info("select slot: %d", slot)
        return slot
    
    async def get_slot_config(self):
        logger.info("get slot config: %s", json.dumps(config))
        return config

    async def save_load(self, is_save):
        if len(config) == 0:
            _load_config()
        slot = config["selectedSlot"]
        backup = SAVE_FILE_NAME+"."+str(slot)+".bak"
        if is_save:
            shutil.copy(SAVE_FILE_PATH+SAVE_FILE_NAME, SAVE_FILE_PATH+backup)
            logger.info("Save %d backup completed.", slot)
            config["selectedSlot"] = slot
            config["slots"][slot-1]["lastModified"] = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
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


    # Asyncio-compatible long-running code, executed in a task when the plugin is loaded
    async def _main(self):
        logger.info("Dark Souls 3 Save Backup Plugin is running.")
        _load_config()
    
    async def _unload(self):
        logger.info("DS3 Savebackup is unloading.")
