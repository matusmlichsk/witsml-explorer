import { useRouter } from "next/router";
import { ParsedUrlQuery } from "querystring";
import React, { useContext, useEffect, useState } from "react";
import {
  SelectLogTypeAction,
  SelectObjectAction,
  SelectObjectGroupAction,
  SelectServerAction,
  SelectWellAction,
  SelectWellboreAction,
  SetFilterAction
} from "../contexts/navigationActions";
import NavigationContext, { NavigationState } from "../contexts/navigationContext";
import NavigationType from "../contexts/navigationType";
import { ObjectType } from "../models/objectType";
import { Server } from "../models/server";
import Well from "../models/well";
import Wellbore, { calculateLogTypeDepthId, calculateLogTypeTimeId, getObjectFromWellbore } from "../models/wellbore";
import { truncateAbortHandler } from "../services/apiClient";
import NotificationService from "../services/notificationService";
import WellboreService from "../services/wellboreService";
import { WITSML_INDEX_TYPE_MD } from "./Constants";

const Routing = (): React.ReactElement => {
  const { dispatchNavigation, navigationState } = useContext(NavigationContext);
  const {
    selectedServer,
    servers,
    wells,
    selectedWell,
    selectedWellbore,
    selectedLog,
    selectedMudLog,
    selectedObjectGroup,
    selectedTrajectory,
    selectedTubular,
    selectedWbGeometry,
    selectedLogTypeGroup
  } = navigationState;
  const router = useRouter();
  const [isSyncingUrlAndState, setIsSyncingUrlAndState] = useState<boolean>(true);
  const [hasSelectedServer, setHasSelectedServer] = useState<boolean>(false);
  const [urlParams, setUrlParams] = useState<QueryParams>(null);
  const [currentQueryParams, setCurrentQueryParams] = useState<QueryParams>(null);

  useEffect(() => {
    //set initial params state
    if (isSyncingUrlAndState) {
      setUrlParams(getQueryParamsFromUrl(router.query));
      setCurrentQueryParams(getQueryParamsFromState(navigationState));
    }
  }, [router]);

  useEffect(() => {
    // update params on navigation state change
    setCurrentQueryParams(getQueryParamsFromState(navigationState));
    const finishedSyncingStateAndUrl = isSyncingUrlAndState && urlParams && isQueryParamsEqual(urlParams, currentQueryParams);
    if (finishedSyncingStateAndUrl) {
      setIsSyncingUrlAndState(false);
    }
  }, [
    selectedServer,
    selectedWell,
    selectedWellbore,
    selectedLog,
    selectedTubular,
    selectedMudLog,
    selectedObjectGroup,
    selectedTrajectory,
    selectedWbGeometry,
    selectedLogTypeGroup
  ]);

  useEffect(() => {
    //update router on params change
    if (!isSyncingUrlAndState) {
      router.push({
        pathname: "/",
        query: { ...currentQueryParams }
      });
    }
  }, [currentQueryParams, isSyncingUrlAndState]);

  useEffect(() => {
    // update selected server when servers are fetched
    if (isSyncingUrlAndState && urlParams) {
      const serverUrl = urlParams.serverUrl;
      if (serverUrl == null) {
        return;
      }
      const server = servers.find((server: Server) => server.url.toLowerCase() === serverUrl.toLowerCase());
      if (server && !selectedServer) {
        if (hasSelectedServer) {
          // finish syncing if routing has already attempted to navigate to a server (such as on login cancellation)
          setIsSyncingUrlAndState(false);
        } else {
          setHasSelectedServer(true);
          const action: SelectServerAction = { type: NavigationType.SelectServer, payload: { server } };
          dispatchNavigation(action);
        }
      }
    }
  }, [servers, urlParams]);

  useEffect(() => {
    // update selected well when wells are fetched
    if (isSyncingUrlAndState && urlParams) {
      const wellUid = urlParams.wellUid;
      if (wellUid != null && !selectedWell && wells.length > 0) {
        const well: Well = wells.find((w: Well) => w.uid === wellUid);
        if (well) {
          const selectWellAction: SelectWellAction = { type: NavigationType.SelectWell, payload: { well, wellbores: well.wellbores } };
          dispatchNavigation(selectWellAction);
        } else {
          NotificationService.Instance.alertDispatcher.dispatch({
            serverUrl: new URL(selectedServer?.url),
            message: `Well with UID ${wellUid} was not found on the current server.`,
            isSuccess: false
          });
          setIsSyncingUrlAndState(false);
        }
      } else if (wellUid == null) {
        setIsSyncingUrlAndState(false);
      }
    }
  }, [wells]);

  useEffect(() => {
    if (isSyncingUrlAndState && selectedWell) {
      const setFilterAction: SetFilterAction = { type: NavigationType.SetFilter, payload: { filter: { ...navigationState.selectedFilter, wellName: selectedWell.name } } };
      dispatchNavigation(setFilterAction);
    }
  }, [selectedWell]);

  useEffect(() => {
    // fetch wellbore objects once a well is selected
    const shouldNavigateToWellbore = isSyncingUrlAndState && selectedWell && urlParams?.wellboreUid && !selectedWellbore;
    if (shouldNavigateToWellbore) {
      const wellboreUid = urlParams.wellboreUid.toString();
      const controller = new AbortController();

      const getChildren = async () => {
        const wellbore: Wellbore = selectedWell.wellbores.find((wb: Wellbore) => wb.uid === wellboreUid);
        if (wellbore) {
          const wellboreObjects = await WellboreService.getWellboreObjects(selectedWell.uid, wellboreUid);
          const selectWellbore: SelectWellboreAction = {
            type: NavigationType.SelectWellbore,
            payload: { well: selectedWell, wellbore, ...wellboreObjects }
          } as SelectWellboreAction;
          dispatchNavigation(selectWellbore);
        } else {
          NotificationService.Instance.alertDispatcher.dispatch({
            serverUrl: new URL(selectedServer?.url),
            message: `Wellbore with UID ${wellboreUid} was not found on the ${selectedWell.name} well.`,
            isSuccess: false
          });
          setIsSyncingUrlAndState(false);
        }
      };

      getChildren().catch(truncateAbortHandler);
      return () => {
        controller.abort();
      };
    } else if (selectedWell && urlParams?.wellboreUid == null) {
      setIsSyncingUrlAndState(false);
    }
  }, [selectedWell]);

  useEffect(() => {
    if (isSyncingUrlAndState && selectedWellbore) {
      const group = urlParams?.group as ObjectType;
      const objectUid = urlParams?.objectUid;
      if (objectUid != null) {
        const object = getObjectFromWellbore(selectedWellbore, objectUid, group);
        if (object != null) {
          const action: SelectObjectAction = {
            type: NavigationType.SelectObject,
            payload: { object, well: selectedWell, wellbore: selectedWellbore, objectType: group }
          };
          dispatchNavigation(action);
        } else {
          NotificationService.Instance.alertDispatcher.dispatch({
            serverUrl: new URL(selectedServer?.url),
            message: `Unable to select an object of type ${group} with UID ${objectUid} on the ${selectedWellbore.name} wellbore as the object was not found.`,
            isSuccess: false
          });
        }
      } else if (group != null) {
        if (urlParams.logType != null) {
          const logTypeGroup = urlParams.logType == "depth" ? calculateLogTypeDepthId(selectedWellbore) : calculateLogTypeTimeId(selectedWellbore);
          const action: SelectLogTypeAction = { type: NavigationType.SelectLogType, payload: { well: selectedWell, wellbore: selectedWellbore, logTypeGroup: logTypeGroup } };
          dispatchNavigation(action);
        } else {
          const action: SelectObjectGroupAction = {
            type: NavigationType.SelectObjectGroup,
            payload: { objectType: group, well: selectedWell, wellbore: selectedWellbore }
          };
          dispatchNavigation(action);
        }
      }
      setIsSyncingUrlAndState(false);
    }
  }, [selectedWellbore]);

  return <></>;
};

const isQueryParamsEqual = (urlQp: QueryParams, stateQp: QueryParams): boolean => {
  if (Object.keys(urlQp).length !== Object.keys(urlQp).length) {
    return false;
  }

  return (Object.keys(urlQp) as (keyof typeof urlQp)[]).every((key) => {
    return Object.prototype.hasOwnProperty.call(stateQp, key) && urlQp[key] === stateQp[key];
  });
};

export const logTypeToQuery = (logType: string) => {
  return logType.includes(WITSML_INDEX_TYPE_MD) ? "depth" : "time";
};

export const getQueryParamsFromState = (state: NavigationState): QueryParams => {
  return {
    ...(state.selectedServer && { serverUrl: state.selectedServer.url }),
    ...(state.selectedWell && { wellUid: state.selectedWell.uid }),
    ...(state.selectedWellbore && { wellboreUid: state.selectedWellbore.uid }),
    ...(state.selectedObjectGroup && { group: state.selectedObjectGroup }),
    ...(state.selectedLogTypeGroup && { logType: logTypeToQuery(state.selectedLogTypeGroup) }),
    ...(state.selectedLog && { objectUid: state.selectedLog.uid }),
    ...(state.selectedTrajectory && { objectUid: state.selectedTrajectory.uid }),
    ...(state.selectedTubular && { objectUid: state.selectedTubular.uid }),
    ...(state.selectedMudLog && { objectUid: state.selectedMudLog.uid }),
    ...(state.selectedWbGeometry && { objectUid: state.selectedWbGeometry.uid })
  };
};

export const getQueryParamsFromUrl = (query: ParsedUrlQuery): QueryParams => {
  return {
    ...(query.serverUrl && { serverUrl: query.serverUrl.toString() }),
    ...(query.wellUid && { wellUid: query.wellUid.toString() }),
    ...(query.wellboreUid && { wellboreUid: query.wellboreUid.toString() }),
    ...(query.group && { group: query.group.toString() }),
    ...(query.logType && { logType: query.logType.toString() }),
    ...(query.objectUid && { objectUid: query.objectUid.toString() })
  };
};

export interface QueryParams {
  serverUrl: string;
  wellUid?: string;
  wellboreUid?: string;
  group?: string;
  logType?: string;
  objectUid?: string;
}

export default Routing;
