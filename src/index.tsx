import {
  ButtonItem,
  definePlugin,
  //DialogButton,
  PanelSection,
  PanelSectionRow,
  ServerAPI,
  staticClasses,
  DropdownItem,
  DropdownOption,
  Router,
  DialogButton
} from "decky-frontend-lib";
import { VFC, useState, useEffect, useMemo } from "react";
import { FaDragon } from "react-icons/fa";

//import logo from "../assets/logo.png";

interface BackupMethodArgs {
  is_save: boolean;
}

interface SlotConfig {
  selectedSlot: number;
  slots: SlotInfo[]
}
interface SlotInfo {
  lastModified: string;
}

const Content: VFC<{ serverAPI: ServerAPI }> = ({ serverAPI }) => {
  const [statusText, setStatusText] = useState<string | undefined>();

  const onSaveLoad = async (save: boolean) => {
    const result = await serverAPI.callPluginMethod<BackupMethodArgs, string>(
      "save_load",
      {
        is_save: save,
      }
    );
    if (result.success) {
      setStatusText(result.result);
      const configresult = await serverAPI.callPluginMethod<any, SlotConfig>("get_slot_config", {});
      if (configresult.success) {
        setSlotConfig(configresult.result);
      }
    }
  };

  const [slotConfig, setSlotConfig] = useState<SlotConfig | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);

  const onSelectionChange = async (slot: number) => {
    setSelectedSlot(slot);
    await serverAPI.callPluginMethod("select_slot", { slot: slot })
  }

  useEffect(() => {
    serverAPI.callPluginMethod<any, SlotConfig>("get_slot_config", {}).then(result => {
      if (result.success) {
        setSlotConfig(result.result);
        setSelectedSlot(result.result.selectedSlot);
      }
    });
  }, []);

  const slotOptions = useMemo(
    (): DropdownOption[] => {
      let slotCount = slotConfig?.slots?.length;
      if (slotCount != null && typeof (slotCount) != 'undefined') {
        let slots = [];
        for (let i = 0; i < slotCount; i++) {
          let timestamp = slotConfig?.slots[i]?.lastModified;
          if (timestamp === null || typeof (timestamp) === "undefined") {
            timestamp = "Empty"
          }
          let entry = { data: i + 1, label: (i + 1).toString() + " " + timestamp };
          slots.push(entry)
        }
        return slots;
      }
      else {
        return [
          { data: 1, label: "1 Empty" },
          { data: 2, label: "2 Empty" },
          { data: 3, label: "3 Empty" },
        ]
      }
    },
    [slotConfig]
  );


  return (
    <PanelSection title="Save Backup">
      <PanelSectionRow>
        <ButtonItem
          layout="below"
          onClick={() => onSaveLoad(true)}
        >
          Save
        </ButtonItem>
      </PanelSectionRow>

      <PanelSectionRow>
        <ButtonItem
          layout="below"
          onClick={() => onSaveLoad(false)}
        >
          Load
        </ButtonItem>
      </PanelSectionRow>
      <PanelSectionRow>
        <div>{statusText}</div>
      </PanelSectionRow>
      <PanelSectionRow>
        <DropdownItem
          label="Save Slot #"
          menuLabel="Slot #"
          strDefaultLabel="Select a slot"
          rgOptions={slotOptions}
          selectedOption={selectedSlot}
          onChange={(data) => onSelectionChange(data.data)}
        />
      </PanelSectionRow>

      <PanelSectionRow>
        <ButtonItem
          layout="below"
          onClick={() => {
            Router.CloseSideMenus();
            Router.Navigate("/sdh-ds3-savebackup-cloud");
          }}
        >Open Cloud Service</ButtonItem>
      </PanelSectionRow>

    </PanelSection>
  );
};

const CloudServicePlugin: VFC<{ serverAPI: ServerAPI }> = ({ serverAPI }) => {

  const [status, setStatus] = useState<string | null>(null);
  const [btnDisabled, setBtnDisabled] = useState<boolean>(false);

  const onSignInClicked = async () => {
    serverAPI.callPluginMethod<any, string>("signin", {}).then(result => {
      console.log(result)
      if (result.success) {
        let uri = result.result;
        console.log(uri)
        if (uri != "") {
          Router.NavigateToExternalWeb(uri);
        }
      }
    });
  }



  const onUploadClicked = async () => {
    setBtnDisabled(true);
    setPollStatus(true);
    serverAPI.callPluginMethod<any, string>("upload", {}).then(result => {
      console.log(result);
      // setBtnDisabled(false);
    });
  }

  const onDownloadClicked = async () => {
    setBtnDisabled(true);
    setPollStatus(true);
    serverAPI.callPluginMethod<any, string>("download", {}).then(result => {
      console.log(result);
      // setBtnDisabled(false);
    });
  }

  const [pollStatus, setPollStatus] = useState<boolean>(false);

  useEffect(() => {
    if (pollStatus) {
      const interval = setInterval(() => {
        serverAPI.callPluginMethod<any, string>("get_status", {}).then(result => {
          if (result.success) {
            setStatus(result.result);
            let strs = result.result.split("\n");
            let s = strs.pop();
            if (s == "done") {
              setPollStatus(false);
              setBtnDisabled(false);
            }
          }
          else {
            setPollStatus(false);
            setBtnDisabled(false);
          }
        })
      }, 3000);
      return () => clearInterval(interval);
    } else {
      return () => { };
    }
  }, [pollStatus]);

  return (
    <div style={{ marginTop: "50px", color: "white" }}>

      <div>Status:</div>
      <div>
        <pre>{status}</pre>
      </div>

      <DialogButton onClick={() => onSignInClicked()}>
        Sign in Microsoft
      </DialogButton>
      <DialogButton onClick={() => onUploadClicked()} disabled={btnDisabled}>
        Upload
      </DialogButton>
      <DialogButton onClick={() => onDownloadClicked()} disabled={btnDisabled}>
        Download
      </DialogButton>
    </div>
  );
};

export default definePlugin((serverApi: ServerAPI) => {
  serverApi.routerHook.addRoute("/sdh-ds3-savebackup-cloud", () => {
    return <CloudServicePlugin serverAPI={serverApi}></CloudServicePlugin>
  }, {
    exact: true
  });


  return {
    title: <div className={staticClasses.Title}>DS3 SaveBackup</div>,
    content: <Content serverAPI={serverApi} />,
    icon: <FaDragon />,
    onDismount() {
      serverApi.routerHook.removeRoute("/sdh-ds3-savebackup-cloud");
    },
  };
});
